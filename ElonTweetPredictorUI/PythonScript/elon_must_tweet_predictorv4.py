"""
================================================================================
 Elon Musk Weekly Tweet Count — Bayesian Predictor w/ News Event Factor
 REST polling every 5 minutes via XTracker API
 Discord webhook notification on every new tweet detected
================================================================================
"""

import asyncio
import aiohttp
import csv
import json
import logging
import os
import pickle
import signal
import sys
from collections import defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Optional

from scipy.stats import norm

import numpy as np
import pandas as pd
import pytz
from vaderSentiment.vaderSentiment import SentimentIntensityAnalyzer

# ──────────────────────────────────────────────────────────────────────────────
# CONFIGURATION
# ──────────────────────────────────────────────────────────────────────────────

XTRACKER_BASE_URL     = "https://xtracker.polymarket.com/api"
ELON_HANDLE           = "elonmusk"
PLATFORM              = "X"

DATA_DIR              = os.environ.get("DATA_DIR", ".")
CSV_FILE              = os.path.join(DATA_DIR, "elonmusk_tweet_history.csv")
MODEL_FILE            = os.path.join(DATA_DIR, "bayesian_model.pkl")
LOG_FILE              = os.path.join(DATA_DIR, "tweet_predictor.log")
EVENT_LOG_FILE        = os.path.join(DATA_DIR, "event_factors.log")
STATUS_FILE           = os.path.join(DATA_DIR, "status.json")
JSON_LOG_FILE         = os.path.join(DATA_DIR, "logs.json")
RELOAD_FLAG_FILE      = os.path.join(DATA_DIR, "reload.flag")

NEWS_API_KEY          = os.environ.get("NEWS_API_KEY", "")
NEWS_API_BASE_URL     = "https://newsapi.org/v2/everything"
NEWS_SCAN_INTERVAL    = 1800
NEWS_LOOKBACK_HOURS   = 12
MAX_ARTICLES_PER_SCAN = 30

DISCORD_WEBHOOK_URL   = os.environ.get("DISCORD_WEBHOOK_URL", "")

DEVIATION_Z_THRESHOLD = 1.75
POLL_INTERVAL_SEC     = 300
BET_CHECK_INTERVAL    = 300
EVENT_FACTOR_DECAY    = 0.90
EVENT_FACTOR_MAX      = 0.40

EST_TZ                = pytz.timezone("America/New_York")

# ──────────────────────────────────────────────────────────────────────────────
# LOGGING
# ──────────────────────────────────────────────────────────────────────────────

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s — %(message)s",
    handlers=[
        logging.FileHandler(LOG_FILE, encoding="utf-8"),
        logging.StreamHandler(sys.stdout),
    ],
)
logger = logging.getLogger("TweetPredictor")

event_logger = logging.getLogger("EventFactor")
event_logger.setLevel(logging.INFO)
_efh = logging.FileHandler(EVENT_LOG_FILE, encoding="utf-8")
_efh.setFormatter(logging.Formatter("%(asctime)s — %(message)s"))
event_logger.addHandler(_efh)
event_logger.propagate = True


# ──────────────────────────────────────────────────────────────────────────────
# JSON LOG HANDLER  (structured logs for .NET management app)
# ──────────────────────────────────────────────────────────────────────────────

class JsonLogHandler(logging.Handler):
    """Appends every log record as a JSON line to JSON_LOG_FILE.
    The .NET web app reads this file to render logs in the dashboard.
    Keeps at most MAX_JSON_LOG_LINES to prevent unbounded growth.
    """

    MAX_JSON_LOG_LINES = 5000

    def __init__(self, filepath: str = JSON_LOG_FILE):
        super().__init__()
        self.filepath = Path(filepath)

    def emit(self, record: logging.LogRecord) -> None:
        try:
            entry = {
                "timestamp": datetime.now(timezone.utc).isoformat(),
                "level":     record.levelname,
                "logger":    record.name,
                "message":   self.format(record),
            }
            with open(self.filepath, "a", encoding="utf-8") as f:
                f.write(json.dumps(entry) + "\n")
        except Exception:
            pass

    def trim(self) -> None:
        """Keep only the last MAX_JSON_LOG_LINES lines."""
        try:
            if not self.filepath.exists():
                return
            lines = self.filepath.read_text(encoding="utf-8").splitlines()
            if len(lines) > self.MAX_JSON_LOG_LINES:
                keep = lines[-self.MAX_JSON_LOG_LINES:]
                self.filepath.write_text("\n".join(keep) + "\n", encoding="utf-8")
        except Exception:
            pass


_json_log_handler = JsonLogHandler()
_json_log_handler.setFormatter(logging.Formatter("%(message)s"))
logging.getLogger().addHandler(_json_log_handler)


# ──────────────────────────────────────────────────────────────────────────────
# NEWS KEYWORD TAXONOMY
# ──────────────────────────────────────────────────────────────────────────────

EVENT_TAXONOMY = {
    "geopolitical": {
        "queries":   ["war", "sanctions", "NATO", "China Taiwan", "Middle East",
                      "Russia Ukraine", "election", "coup", "treaty"],
        "keywords":  ["war", "conflict", "sanction", "nato", "military",
                      "invasion", "ceasefire", "election", "president",
                      "diplomacy", "nuclear", "ally", "taiwan", "ukraine",
                      "russia", "israel", "iran", "coup", "regime"],
        "weight":    0.35,
        "direction": +1,
    },
    "economic": {
        "queries":   ["Federal Reserve interest rate", "inflation CPI",
                      "stock market crash", "recession", "cryptocurrency bitcoin",
                      "tariff trade war", "GDP", "bank collapse"],
        "keywords":  ["fed", "inflation", "rate hike", "recession", "gdp",
                      "market crash", "bitcoin", "crypto", "tariff",
                      "unemployment", "cpi", "interest rate", "bank",
                      "treasury", "s&p", "nasdaq", "dow"],
        "weight":    0.30,
        "direction": +1,
    },
    "tesla": {
        "queries":   ["Tesla stock", "Tesla recall", "Tesla earnings",
                      "Tesla Cybertruck", "Tesla Autopilot", "Tesla layoffs",
                      "TSLA"],
        "keywords":  ["tesla", "tsla", "cybertruck", "autopilot", "fsd",
                      "recall", "layoff", "earnings", "delivery", "gigafactory",
                      "model 3", "model y", "model s", "roadster", "semi"],
        "weight":    0.25,
        "direction": +1,
    },
    "spacex_xai": {
        "queries":   ["SpaceX launch", "Starship", "xAI Grok", "Neuralink",
                      "Boring Company"],
        "keywords":  ["spacex", "starship", "falcon", "rocket", "launch",
                      "xai", "grok", "neuralink", "boring company", "starlink"],
        "weight":    0.10,
        "direction": +1,
    },
    "personal_legal": {
        "queries":   ["Elon Musk lawsuit", "Elon Musk controversy",
                      "Elon Musk SEC", "Elon Musk court"],
        "keywords":  ["lawsuit", "sued", "court", "sec", "investigation",
                      "controversy", "scandal", "fired", "arrested"],
        "weight":    0.10,
        "direction": +1,
    },
}

# ──────────────────────────────────────────────────────────────────────────────
# CSV MANAGER
# ──────────────────────────────────────────────────────────────────────────────

class CSVManager:
    HEADERS = ["DateTime_UTC", "Cumulative_Tweet_Count"]

    def __init__(self, filepath: str = CSV_FILE):
        self.filepath = Path(filepath)
        self._ensure_file()

    def _ensure_file(self) -> None:
        if not self.filepath.exists():
            with open(self.filepath, "w", newline="", encoding="utf-8") as f:
                csv.DictWriter(f, fieldnames=self.HEADERS).writeheader()
            logger.info("Created CSV: %s", self.filepath)

    def append_row(self, datetime_utc: datetime, cumulative_count: int) -> None:
        row = {
            "DateTime_UTC":           datetime_utc.strftime("%Y-%m-%dT%H:%M:%SZ"),
            "Cumulative_Tweet_Count": cumulative_count,
        }
        with open(self.filepath, "a", newline="", encoding="utf-8") as f:
            csv.DictWriter(f, fieldnames=self.HEADERS).writerow(row)
        logger.debug("CSV row written: %s", row)

    def load_dataframe(self) -> pd.DataFrame:
        df = pd.read_csv(self.filepath)
        df["DateTime_UTC"] = pd.to_datetime(df["DateTime_UTC"], format="mixed", utc=True)
        df.sort_values("DateTime_UTC", inplace=True)
        df.reset_index(drop=True, inplace=True)
        return df

    def latest_cumulative_count(self) -> int:
        df = self.load_dataframe()
        return 0 if df.empty else int(df["Cumulative_Tweet_Count"].iloc[-1])

    def latest_timestamp(self) -> Optional[datetime]:
        df = self.load_dataframe()
        return None if df.empty else df["DateTime_UTC"].iloc[-1].to_pydatetime()


# ──────────────────────────────────────────────────────────────────────────────
# TEMPORAL PATTERN ANALYZER
# ──────────────────────────────────────────────────────────────────────────────

class TemporalPatternAnalyzer:
    DAY_NAMES = ["Monday", "Tuesday", "Wednesday", "Thursday",
                 "Friday", "Saturday", "Sunday"]

    def __init__(self, csv_manager: CSVManager):
        self.csv = csv_manager

    def analyze_and_log(self) -> dict:
        df = self.csv.load_dataframe()
        if df.empty or len(df) < 2:
            logger.warning("TemporalPatternAnalyzer: not enough data yet.")
            return {i: 1 / 7 for i in range(7)}

        df_est = df.copy()
        df_est["DateTime_EST"] = df_est["DateTime_UTC"].dt.tz_convert(EST_TZ)
        df_est["DayOfWeek"]    = df_est["DateTime_EST"].dt.dayofweek
        df_est["Hour"]         = df_est["DateTime_EST"].dt.hour
        df_est["DailyCount"]   = (
            df_est["Cumulative_Tweet_Count"].diff()
            .fillna(df_est["Cumulative_Tweet_Count"].iloc[0])
            .clip(lower=0).astype(int)
        )

        dow_weights = self._day_of_week_analysis(df_est)
        self._hourly_analysis(df_est)
        self._inactivity_analysis(df)
        self._weekly_summary(df_est)
        return dow_weights

    def _day_of_week_analysis(self, df: pd.DataFrame) -> dict:
        dow_totals  = df.groupby("DayOfWeek")["DailyCount"].sum()
        grand_total = dow_totals.sum()
        logger.info("=" * 60)
        logger.info("TEMPORAL PATTERN — Day-of-Week Distribution (EST)")
        logger.info("=" * 60)
        weights = {}
        for d in range(7):
            count  = int(dow_totals.get(d, 0))
            weight = count / grand_total if grand_total > 0 else 1 / 7
            weights[d] = weight
            bar = "█" * int(weight * 40)
            logger.info(
                "  %-9s │ %s %.1f%%  (%d tweets)",
                self.DAY_NAMES[d], bar.ljust(40), weight * 100, count,
            )
        logger.info("=" * 60)
        return weights

    def _hourly_analysis(self, df: pd.DataFrame) -> None:
        hourly    = df.groupby("Hour")["DailyCount"].sum()
        peak_hour = int(hourly.idxmax()) if not hourly.empty else -1
        logger.info("TEMPORAL PATTERN — Hourly Distribution (EST)")
        logger.info("-" * 60)
        for h in range(24):
            count = int(hourly.get(h, 0))
            bar   = "▪" * min(count, 50)
            logger.info("  %02d:00 │ %s %d", h, bar.ljust(50), count)
        logger.info("  ► Peak hour (EST): %02d:00", peak_hour)
        logger.info("-" * 60)

    def _inactivity_analysis(self, df: pd.DataFrame) -> None:
        if len(df) < 2:
            return
        gaps = df["DateTime_UTC"].diff().dropna().dt.total_seconds() / 3600
        logger.info("TEMPORAL PATTERN — Inactivity Gap Statistics")
        logger.info("-" * 60)
        logger.info("  Mean gap              : %.2f hours", gaps.mean())
        logger.info("  Median gap            : %.2f hours", gaps.median())
        logger.info("  Max gap (longest)     : %.2f hours", gaps.max())
        logger.info("  Std dev of gaps       : %.2f hours", gaps.std())
        threshold = gaps.mean() + 2 * gaps.std()
        long_gaps = gaps[gaps > threshold]
        logger.info(
            "  Unusually long (>%.1f hrs): %d occurrences",
            threshold, len(long_gaps),
        )
        logger.info("-" * 60)

    def _weekly_summary(self, df: pd.DataFrame) -> None:
        df = df.copy()
        df["Week"] = df["DateTime_EST"].dt.isocalendar().week.astype(int)
        df["Year"] = df["DateTime_EST"].dt.year
        weekly     = df.groupby(["Year", "Week"])["DailyCount"].sum()
        if weekly.empty:
            return
        logger.info("TEMPORAL PATTERN — Weekly Tweet Summary")
        logger.info("-" * 60)
        for (yr, wk), total in weekly.items():
            logger.info("    %d-W%02d │ %d tweets", yr, wk, int(total))
        logger.info(
            "  Mean: %.1f  |  Std: %.1f  |  Min: %d  |  Max: %d",
            weekly.mean(), weekly.std(),
            int(weekly.min()), int(weekly.max()),
        )
        logger.info("-" * 60)


# ──────────────────────────────────────────────────────────────────────────────
# DEVIATION DETECTOR
# ──────────────────────────────────────────────────────────────────────────────

class DeviationDetector:
    """
    Compares actual daily tweet count against the expected daily count
    derived from posterior_mean x day-of-week weight and returns a Z-score.
    Triggers a news scan when |Z| >= DEVIATION_Z_THRESHOLD.
    """

    def __init__(self, csv_manager: CSVManager):
        self.csv = csv_manager
        self._historical_daily: list[float] = []
        self._refresh_history()

    def _refresh_history(self) -> None:
        df = self.csv.load_dataframe()
        if df.empty or len(df) < 2:
            return
        df["DateTime_EST"] = df["DateTime_UTC"].dt.tz_convert(EST_TZ)
        df["Date_EST"]     = df["DateTime_EST"].dt.date
        df["DailyCount"]   = (
            df["Cumulative_Tweet_Count"].diff()
            .fillna(df["Cumulative_Tweet_Count"].iloc[0])
            .clip(lower=0)
        )
        self._historical_daily = (
            df.groupby("Date_EST")["DailyCount"].sum().tolist()
        )

    def compute_z_score(self, actual: int, expected: float) -> float:
        self._refresh_history()
        if len(self._historical_daily) >= 5:
            std = max(float(np.std(self._historical_daily)), 1.0)
            return (actual - expected) / std
        if expected > 0:
            return (actual - expected) / max(expected * 0.3, 1.0)
        return 0.0

    def evaluate(
        self,
        day_of_week:    int,
        actual_count:   int,
        posterior_mean: float,
        day_weights:    dict,
    ) -> dict:
        weight         = day_weights.get(day_of_week, 1 / 7)
        expected_daily = posterior_mean * weight
        z_score        = self.compute_z_score(actual_count, expected_daily)
        abs_z          = abs(z_score)
        is_deviation   = abs_z >= DEVIATION_Z_THRESHOLD
        day_name       = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"][day_of_week]

        result = {
            "day":                 day_name,
            "actual_count":        actual_count,
            "expected_count":      round(expected_daily, 1),
            "z_score":             round(z_score, 3),
            "abs_z_score":         round(abs_z, 3),
            "is_high_deviation":   is_deviation,
            "deviation_direction": "ABOVE" if z_score > 0 else "BELOW",
        }

        if is_deviation:
            logger.warning(
                "⚠️  DEVIATION on %s — actual: %d  expected: %.1f  "
                "Z=%.2f (%s) → news scan triggered",
                day_name, actual_count, expected_daily,
                z_score, result["deviation_direction"],
            )
        else:
            logger.info(
                "✅ Normal activity on %s — actual: %d  expected: %.1f  Z=%.2f",
                day_name, actual_count, expected_daily, z_score,
            )
        return result


# ──────────────────────────────────────────────────────────────────────────────
# NEWS SCANNER
# ──────────────────────────────────────────────────────────────────────────────

class NewsScanner:
    def __init__(self, session: aiohttp.ClientSession):
        self._session      = session
        self._api_key      = NEWS_API_KEY
        self._last_scan_at: Optional[datetime] = None

        if not self._api_key or self._api_key == "your_newsapi_key_here":
            logger.warning(
                "NEWS_API_KEY is a placeholder — replace it in CONFIGURATION."
            )

    async def fetch(
        self,
        lookback_hours: int = NEWS_LOOKBACK_HOURS,
        categories:     Optional[list[str]] = None,
    ) -> list[dict]:
        if not self._api_key or self._api_key == "your_newsapi_key_here":
            logger.warning("Skipping news scan: NEWS_API_KEY not set.")
            return []

        categories = categories or list(EVENT_TAXONOMY.keys())
        from_str   = (
            datetime.now(timezone.utc) - timedelta(hours=lookback_hours)
        ).strftime("%Y-%m-%dT%H:%M:%SZ")

        all_articles: list[dict] = []
        seen_urls:    set[str]   = set()

        for category in categories:
            taxonomy = EVENT_TAXONOMY[category]
            query    = " OR ".join(f'"{q}"' for q in taxonomy["queries"])
            params   = {
                "q":        query,
                "from":     from_str,
                "sortBy":   "publishedAt",
                "language": "en",
                "pageSize": MAX_ARTICLES_PER_SCAN,
                "apiKey":   self._api_key,
            }

            try:
                async with self._session.get(
                    NEWS_API_BASE_URL,
                    params=params,
                    timeout=aiohttp.ClientTimeout(total=15),
                ) as resp:
                    if resp.status == 429:
                        logger.warning(
                            "NewsAPI rate limit — skipping: %s", category
                        )
                        continue
                    resp.raise_for_status()
                    data = await resp.json()

                for art in data.get("articles", []):
                    url = art.get("url", "")
                    if url in seen_urls:
                        continue
                    seen_urls.add(url)
                    all_articles.append({
                        "title":       art.get("title", ""),
                        "description": art.get("description", "") or "",
                        "publishedAt": art.get("publishedAt", ""),
                        "source":      art.get("source", {}).get("name", ""),
                        "url":         url,
                        "category":    category,
                    })

                logger.info(
                    "NewsAPI %-16s : %d articles",
                    category, len(data.get("articles", [])),
                )
                await asyncio.sleep(0.3)

            except asyncio.TimeoutError:
                logger.warning("NewsAPI timeout: %s", category)
            except Exception as exc:
                logger.error("NewsAPI error (%s): %s", category, exc)

        self._last_scan_at = datetime.now(timezone.utc)
        logger.info(
            "News scan complete — %d unique articles.", len(all_articles)
        )
        return all_articles


# ──────────────────────────────────────────────────────────────────────────────
# EVENT FACTOR ANALYZER
# ──────────────────────────────────────────────────────────────────────────────

class EventFactorAnalyzer:
    def __init__(self):
        self._vader = SentimentIntensityAnalyzer()

    def analyze(self, articles: list[dict]) -> dict:
        if not articles:
            return self._empty_result()

        category_accumulator: dict[str, list[float]] = defaultdict(list)
        article_scores:       list[dict]             = []

        for article in articles:
            category  = article["category"]
            taxonomy  = EVENT_TAXONOMY[category]
            direction = taxonomy["direction"]
            weight    = taxonomy["weight"]

            text      = f"{article['title']} {article['description']}".lower()
            kw_hits   = sum(1 for kw in taxonomy["keywords"] if kw in text)
            relevance = min(
                kw_hits / max(len(taxonomy["keywords"]) * 0.25, 1), 1.0
            )
            sentiment = self._vader.polarity_scores(
                f"{article['title']} {article['description']}"
            )["compound"]

            signed_score = (
                relevance * direction * (0.5 + 0.5 * abs(sentiment)) * weight
            )
            category_accumulator[category].append(signed_score)
            article_scores.append({
                "title":        article["title"],
                "source":       article["source"],
                "category":     category,
                "relevance":    round(relevance, 3),
                "sentiment":    round(sentiment, 3),
                "signed_score": round(signed_score, 4),
                "url":          article["url"],
            })

        category_scores: dict[str, float] = {}
        total_score = 0.0
        for cat, scores in category_accumulator.items():
            cat_score            = float(np.sum(scores))
            category_scores[cat] = round(cat_score, 4)
            total_score         += cat_score

        n_active     = max(len(category_accumulator), 1)
        raw_factor   = total_score / n_active
        event_factor = float(
            np.clip(raw_factor, -EVENT_FACTOR_MAX, EVENT_FACTOR_MAX)
        )

        article_scores.sort(key=lambda x: abs(x["signed_score"]), reverse=True)

        result = {
            "event_factor":    round(event_factor, 4),
            "raw_factor":      round(raw_factor, 4),
            "category_scores": category_scores,
            "top_articles":    article_scores[:5],
            "total_articles":  len(articles),
            "scan_time_utc":   datetime.now(timezone.utc).isoformat(),
        }
        self._log_result(result)
        return result

    def _log_result(self, result: dict) -> None:
        ef   = result["event_factor"]
        sign = "+" if ef >= 0 else ""
        event_logger.info("=" * 70)
        event_logger.info(
            "EVENT FACTOR UPDATE → %s%.4f  (%s%.1f%% weekly adjustment)",
            sign, ef, sign, ef * 100,
        )
        event_logger.info("  Articles analyzed: %d", result["total_articles"])
        for cat, score in result["category_scores"].items():
            bar = ("▲" if score >= 0 else "▼") * min(int(abs(score) * 20), 20)
            event_logger.info(
                "    %-18s │ %s%.4f  %s",
                cat, "+" if score >= 0 else "", score, bar,
            )
        for i, art in enumerate(result["top_articles"], 1):
            event_logger.info(
                "    %d. [%s] %-50s  score=%+.4f  sentiment=%+.3f",
                i, art["category"].upper()[:12], art["title"][:50],
                art["signed_score"], art["sentiment"],
            )
        event_logger.info("=" * 70)

    @staticmethod
    def _empty_result() -> dict:
        return {
            "event_factor":    0.0,
            "raw_factor":      0.0,
            "category_scores": {},
            "top_articles":    [],
            "total_articles":  0,
            "scan_time_utc":   datetime.now(timezone.utc).isoformat(),
        }


# ──────────────────────────────────────────────────────────────────────────────
# EVENT FACTOR TRACKER
# ──────────────────────────────────────────────────────────────────────────────

class EventFactorTracker:
    def __init__(self):
        self.current_factor: float         = 0.0
        self.factor_history: list[dict]    = []
        self._last_updated:  Optional[str] = None

    def update(self, new_factor: float, source: str = "scan") -> float:
        alpha               = 0.6
        self.current_factor = (
            alpha * new_factor + (1 - alpha) * self.current_factor
        )
        self.current_factor = float(
            np.clip(self.current_factor, -EVENT_FACTOR_MAX, EVENT_FACTOR_MAX)
        )
        self._last_updated = datetime.now(timezone.utc).isoformat()
        self.factor_history.append({
            "timestamp":  self._last_updated,
            "new_factor": round(new_factor, 4),
            "blended":    round(self.current_factor, 4),
            "source":     source,
        })
        self.factor_history = self.factor_history[-500:]
        event_logger.info(
            "EventFactorTracker — new=%.4f  blended=%.4f  source=%s",
            new_factor, self.current_factor, source,
        )
        return self.current_factor

    def decay(self) -> float:
        before              = self.current_factor
        self.current_factor *= EVENT_FACTOR_DECAY
        if abs(self.current_factor) < 0.005:
            self.current_factor = 0.0
        event_logger.info(
            "EventFactor decay: %.4f → %.4f", before, self.current_factor
        )
        return self.current_factor

    def adjusted_prediction(self, bayesian_mean: float) -> int:
        return max(0, round(bayesian_mean * (1.0 + self.current_factor)))

    def summary(self) -> str:
        sign = "+" if self.current_factor >= 0 else ""
        return (
            f"EventFactor={sign}{self.current_factor:.4f} "
            f"({sign}{self.current_factor * 100:.1f}% adjustment) "
            f"last_updated={self._last_updated}"
        )


# ──────────────────────────────────────────────────────────────────────────────
# STATUS WRITER  (writes status.json for .NET management app)
# ──────────────────────────────────────────────────────────────────────────────

class StatusWriter:
    """Writes a status.json snapshot to the shared data volume.
    The .NET management web app reads this file for the dashboard.
    """

    def __init__(self, filepath: str = STATUS_FILE):
        self.filepath = Path(filepath)
        self._tmp     = Path(f"{filepath}.tmp")

    def write(
        self,
        *,
        state:            str   = "running",
        cumulative_count: int   = 0,
        tweets_this_week: int   = 0,
        prediction:       Optional[dict] = None,
        active_trackings: Optional[list] = None,
        posts_in_windows: Optional[dict] = None,
        model_info:       Optional[dict] = None,
    ) -> None:
        pred = prediction or {}
        status = {
            "updated_at":       datetime.now(timezone.utc).isoformat(),
            "state":            state,
            "cumulative_count": cumulative_count,
            "tweets_this_week": tweets_this_week,
            "prediction": {
                "weekly_total":       pred.get("predicted_weekly_total", 0),
                "bayesian_total":     pred.get("bayesian_weekly_total", 0),
                "ci_lower":           pred.get("adjusted_ci_95_lower", 0),
                "ci_upper":           pred.get("adjusted_ci_95_upper", 0),
                "event_factor":       pred.get("event_factor", 0.0),
                "event_adjustment_pct": pred.get("event_adjustment_pct", 0.0),
                "days_observed":      pred.get("days_observed", 0),
                "days_remaining":     pred.get("days_remaining", 7),
                "posterior_mean":     pred.get("posterior_mean", 0.0),
                "posterior_std":      pred.get("posterior_std", 0.0),
            },
            "active_trackings": [
                {
                    "id":        t.get("id", ""),
                    "title":     t.get("title", ""),
                    "target":    t.get("target"),
                    "startDate": t.get("startDate", ""),
                    "endDate":   t.get("endDate", ""),
                    "tweets_in_window": (posts_in_windows or {}).get(
                        t.get("id", ""), 0
                    ),
                }
                for t in (active_trackings or [])
            ],
            "model": model_info or {},
            "files": {
                "csv":   CSV_FILE,
                "model": MODEL_FILE,
                "log":   LOG_FILE,
            },
        }
        try:
            self._tmp.write_text(
                json.dumps(status, indent=2), encoding="utf-8"
            )
            self._tmp.replace(self.filepath)
        except Exception as exc:
            logger.warning("StatusWriter error: %s", exc)


# ──────────────────────────────────────────────────────────────────────────────
# BAYESIAN TWEET FORECASTER
# ──────────────────────────────────────────────────────────────────────────────

class BayesianTweetForecaster:
    def __init__(
        self,
        prior_mean:           float = 100.0,
        prior_std:            float = 40.0,
        day_weights:          Optional[dict] = None,
        training_weeks:       Optional[list] = None,
        event_factor_tracker: Optional[EventFactorTracker] = None,
    ):
        self.prior_mean            = prior_mean
        self.prior_variance        = prior_std ** 2
        self.day_weights           = day_weights or {i: 1 / 7 for i in range(7)}
        self.training_weeks        = training_weeks or []
        self.event_tracker         = event_factor_tracker or EventFactorTracker()
        self._reset_week_state()

    def _reset_week_state(self) -> None:
        self._observed_days:     dict[int, int] = {}
        self._posterior_mean     = self.prior_mean
        self._posterior_variance = self.prior_variance

    def update(self, day_of_week: int, tweet_count: int) -> dict:
        self._observed_days[day_of_week] = tweet_count

        implied_totals = [
            count / self.day_weights.get(d, 1 / 7)
            for d, count in self._observed_days.items()
            if self.day_weights.get(d, 1 / 7) > 0
        ]

        likelihood_mean     = float(np.mean(implied_totals))
        likelihood_variance = float(np.var(implied_totals)) + max(
            self.prior_variance * 0.05, 25.0
        )

        prec_prior      = 1.0 / self._posterior_variance
        prec_likelihood = 1.0 / likelihood_variance
        prec_posterior  = prec_prior + prec_likelihood

        self._posterior_variance = 1.0 / prec_posterior
        self._posterior_mean     = self._posterior_variance * (
            prec_prior      * self._posterior_mean +
            prec_likelihood * likelihood_mean
        )

        std = float(np.sqrt(self._posterior_variance))
        ef  = self.event_tracker.current_factor

        adjusted_mean  = self.event_tracker.adjusted_prediction(
            self._posterior_mean
        )
        adjusted_lower = max(
            0, round((self._posterior_mean - 1.96 * std) * (1 + ef))
        )
        adjusted_upper = round(
            (self._posterior_mean + 1.96 * std) * (1 + ef)
        )

        result = {
            "bayesian_weekly_total":  round(self._posterior_mean),
            "posterior_mean":         self._posterior_mean,
            "posterior_std":          std,
            "bayesian_ci_95_lower":   max(
                0, round(self._posterior_mean - 1.96 * std)
            ),
            "bayesian_ci_95_upper":   round(
                self._posterior_mean + 1.96 * std
            ),
            "predicted_weekly_total": adjusted_mean,
            "event_factor":           round(ef, 4),
            "event_adjustment_pct":   round(ef * 100, 1),
            "adjusted_ci_95_lower":   adjusted_lower,
            "adjusted_ci_95_upper":   adjusted_upper,
            "days_observed":          len(self._observed_days),
            "days_remaining":         7 - len(self._observed_days),
            "cumulative_so_far":      sum(self._observed_days.values()),
        }

        self._log_prediction(day_of_week, tweet_count, result)
        return result

    def retrain(
        self,
        actual_weekly_total: int,
        new_day_weights:     Optional[dict] = None,
    ) -> None:
        self.training_weeks.append(actual_weekly_total)
        if len(self.training_weeks) >= 2:
            self.prior_mean     = float(np.mean(self.training_weeks))
            self.prior_variance = float(np.var(self.training_weeks)) + 1.0
        else:
            self.prior_mean     = (self.prior_mean + actual_weekly_total) / 2
            self.prior_variance = max(self.prior_variance, 400.0)
        if new_day_weights:
            self.day_weights = new_day_weights
        logger.info(
            "MODEL RETRAINED — actual=%d  new prior: mean=%.1f  std=%.1f  "
            "training_weeks=%d",
            actual_weekly_total, self.prior_mean,
            np.sqrt(self.prior_variance), len(self.training_weeks),
        )
        self._reset_week_state()

    def save(self, filepath: str = MODEL_FILE) -> None:
        state = {
            "prior_mean":         self.prior_mean,
            "prior_variance":     self.prior_variance,
            "day_weights":        self.day_weights,
            "training_weeks":     self.training_weeks,
            "observed_days":      self._observed_days,
            "posterior_mean":     self._posterior_mean,
            "posterior_variance": self._posterior_variance,
            "event_factor":       self.event_tracker.current_factor,
            "event_history":      self.event_tracker.factor_history,
            "event_last_updated": self.event_tracker._last_updated,
        }
        with open(filepath, "wb") as f:
            pickle.dump(state, f)
        logger.info("Model saved → %s", filepath)

    @classmethod
    def load(cls, filepath: str = MODEL_FILE) -> "BayesianTweetForecaster":
        with open(filepath, "rb") as f:
            state = pickle.load(f)
        eft                = EventFactorTracker()
        eft.current_factor = state.get("event_factor", 0.0)
        eft.factor_history = state.get("event_history", [])
        eft._last_updated  = state.get("event_last_updated")
        model = cls(
            prior_mean           = state["prior_mean"],
            prior_std            = float(np.sqrt(state["prior_variance"])),
            day_weights          = state["day_weights"],
            training_weeks       = state["training_weeks"],
            event_factor_tracker = eft,
        )
        model._observed_days      = state["observed_days"]
        model._posterior_mean     = state["posterior_mean"]
        model._posterior_variance = state["posterior_variance"]
        logger.info(
            "Model loaded ← %s  (EventFactor=%.4f)",
            filepath, eft.current_factor,
        )
        return model

    def _log_prediction(self, dow: int, count: int, result: dict) -> None:
        day_name = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"][dow]
        logger.info(
            "🔮 PREDICTION  |  %s: %d tweets  "
            "→  Bayesian: %d  |  EF=%+.4f (%+.1f%%)  "
            "→  Adjusted: %d  [95%% CI: %d–%d]  "
            "(obs: %d/7, so far: %d)",
            day_name, count,
            result["bayesian_weekly_total"],
            result["event_factor"], result["event_adjustment_pct"],
            result["predicted_weekly_total"],
            result["adjusted_ci_95_lower"], result["adjusted_ci_95_upper"],
            result["days_observed"], result["cumulative_so_far"],
        )

# ──────────────────────────────────────────────────────────────────────────────
# OPTIMAL BET CALCULATOR  (Fractional Kelly Criterion)
# ──────────────────────────────────────────────────────────────────────────────

def calculate_optimal_bet(
    predicted_total:  int,
    ci_lower:         int,
    ci_upper:         int,
    target:           int,
    market_yes_price: float = 0.5,
    kelly_fraction:   float = 0.25,
    bankroll:         float = 100.0,
) -> dict:
    """
    Calculates an optimal bet size using Fractional Kelly Criterion.

    Estimates our model's implied probability that the tweet count will
    EXCEED the target, then compares it to the market's implied probability.

    Args:
        predicted_total:  Bayesian point estimate for weekly/window tweets
        ci_lower:         Lower bound of 95% CI
        ci_upper:         Upper bound of 95% CI
        target:           Polymarket threshold (e.g., "more than 400 tweets")
        market_yes_price: Current Polymarket YES price (e.g., 0.62 means 62 cents)
        kelly_fraction:   Fraction of full Kelly to use (0.25 = quarter-Kelly)
        bankroll:         Dollar amount to base sizing on

    Returns:
        dict with recommendation, edge, kelly %, and risk tier
    """
    if ci_upper <= ci_lower:
        return {"recommendation": "⚠️ Insufficient CI data", "bet_pct": 0.0}

    mean_pred = (ci_lower + ci_upper) / 2.0
    sigma     = max((ci_upper - ci_lower) / 3.92, 1.0)

    our_prob_yes = float(1.0 - norm.cdf(target, loc=mean_pred, scale=sigma))
    our_prob_yes = float(np.clip(our_prob_yes, 0.02, 0.98))  # avoid extremes

    market_prob = float(np.clip(market_yes_price, 0.01, 0.99))
    edge        = our_prob_yes - market_prob


    b          = (1.0 - market_prob) / market_prob
    full_kelly = (b * our_prob_yes - (1.0 - our_prob_yes)) / b
    frac_kelly = kelly_fraction * full_kelly
    frac_kelly = float(np.clip(frac_kelly, 0.0, 0.30))  # hard cap at 30% bankroll

    bet_dollars = round(frac_kelly * bankroll, 2)

    abs_edge = abs(edge)
    if abs_edge >= 0.20:
        tier  = "🟢 STRONG edge"
        emoji = "✅"
    elif abs_edge >= 0.10:
        tier  = "🟡 MODERATE edge"
        emoji = "⚠️"
    elif abs_edge >= 0.04:
        tier  = "🟠 WEAK edge"
        emoji = "🔸"
    else:
        tier  = "🔴 NO edge / skip"
        emoji = "❌"

    if edge >= 0.04:
        side = "BUY YES"
    elif edge <= -0.04:
        side       = "BUY NO"
        frac_kelly = kelly_fraction * abs(full_kelly)
        frac_kelly = float(np.clip(frac_kelly, 0.0, 0.30))
        bet_dollars = round(frac_kelly * bankroll, 2)
    else:
        side = "SKIP"

    return {
        "our_prob_yes":    round(our_prob_yes * 100, 1),
        "market_prob_yes": round(market_prob  * 100, 1),
        "edge_pct":        round(edge         * 100, 1),
        "full_kelly_pct":  round(full_kelly   * 100, 1),
        "frac_kelly_pct":  round(frac_kelly   * 100, 1),
        "bet_dollars":     bet_dollars,
        "side":            side,
        "tier":            tier,
        "emoji":           emoji,
        "recommendation":  (
            f"{emoji} **{side}**  —  {tier}\n"
            f"Our P(YES): **{our_prob_yes*100:.1f}%**  vs  "
            f"Market: **{market_prob*100:.1f}%**  "
            f"(Edge: {'+' if edge>=0 else ''}{edge*100:.1f}%)\n"
            f"¼-Kelly size: **{frac_kelly*100:.1f}%** of bankroll  "
            f"(≈ ${bet_dollars:.2f} per $100)"
        ),
    }


# ──────────────────────────────────────────────────────────────────────────────
# DISCORD NOTIFIER
# ──────────────────────────────────────────────────────────────────────────────

class DiscordNotifier:
    def __init__(self, session: aiohttp.ClientSession):
        self._session     = session
        self._webhook_url = DISCORD_WEBHOOK_URL
        self._enabled     = True

    async def send(
        self,
        *,
        new_tweet_count:  int,
        cumulative_count: int,
        tweets_this_week: int,
        prediction:       dict,
        active_trackings: list[dict],
        posts_in_windows: dict[str, int],
    ) -> None:
        if not self._enabled:
            logger.info("Discord disabled — skipping.")
            return

        predicted = prediction["predicted_weekly_total"]
        bayesian  = prediction["bayesian_weekly_total"]
        ci_low    = prediction["adjusted_ci_95_lower"]
        ci_high   = prediction["adjusted_ci_95_upper"]
        ef        = prediction["event_factor"]
        ef_pct    = prediction["event_adjustment_pct"]
        days_obs  = prediction["days_observed"]
        days_rem  = prediction["days_remaining"]
        ef_sign   = "+" if ef >= 0 else ""

        avg_per_day    = tweets_this_week / max(days_obs, 1)
        pace_projected = round(tweets_this_week + avg_per_day * days_rem)

        colour = (
            0x2ECC71 if ef >= 0.05
            else 0xE74C3C if ef <= -0.05
            else 0x3498DB
        )

        now_est = datetime.now(timezone.utc).astimezone(EST_TZ).strftime(
            "%b %d, %Y %I:%M:%S %p EST"
        )

        core_fields: list[dict] = [
            {
                "name":   "🕐 Detected At",
                "value":  now_est,
                "inline": False,
            },
            {
                "name":   "🆕 New Tweets This Poll",
                "value":  f"**+{new_tweet_count}**",
                "inline": True,
            },
            {
                "name":   "📊 Cumulative All-Time",
                "value":  f"**{cumulative_count:,}**",
                "inline": True,
            },
            {
                "name":   "📅 Tweets This Week (Mon–Sun)",
                "value":  f"**{tweets_this_week:,}**",
                "inline": True,
            },
            {
                "name":   "\u200b",
                "value":  "\u200b",
                "inline": False,
            },
            {
                "name":   "🔮 Predicted Weekly Total",
                "value":  f"**{predicted:,}** tweets",
                "inline": True,
            },
            {
                "name":   "📐 95% Confidence Interval",
                "value":  f"{ci_low:,} – {ci_high:,}",
                "inline": True,
            },
            {
                "name":   "🧮 Bayesian Estimate (pre-EF)",
                "value":  f"{bayesian:,}",
                "inline": True,
            },
            {
                "name":   "📰 News Event Factor",
                "value":  f"{ef_sign}{ef:.4f}  ({ef_sign}{ef_pct:.1f}%)",
                "inline": True,
            },
            {
                "name":   "📈 Linear Pace Projection",
                "value":  f"{pace_projected:,}",
                "inline": True,
            },
            {
                "name":   "📆 Days Observed / Remaining",
                "value":  f"{days_obs} observed · {days_rem} remaining",
                "inline": True,
            },
        ]

        embeds: list[dict] = [
            {
                "title":     f"🐦 Elon Tweeted  (+{new_tweet_count})",
                "color":     colour,
                "fields":    core_fields,
                "footer":    {
                    "text": (
                        "Elon Tweet Predictor · "
                        "Bayesian + News Factor Model · "
                        "xtracker.polymarket.com"
                    )
                },
                "timestamp": datetime.now(timezone.utc).isoformat(),
            }
        ]

        if active_trackings:
            for idx, tracking in enumerate(active_trackings, start=1):
                t_id    = tracking.get("id", "")
                t_title = tracking.get("title", f"Market #{idx}")
                target  = tracking.get("target")
                start_r = tracking.get("startDate", "")
                end_r   = tracking.get("endDate", "")

                # ── Window date string and duration ──
                window_str  = "N/A"
                window_days = 7.0
                if start_r and end_r:
                    try:
                        s = (
                            pd.to_datetime(start_r, utc=True)
                            .tz_convert(EST_TZ)
                            .strftime("%b %d %I:%M %p")
                        )
                        e = (
                            pd.to_datetime(end_r, utc=True)
                            .tz_convert(EST_TZ)
                            .strftime("%b %d %I:%M %p EST")
                        )
                        window_str  = f"{s} → {e}"
                        start_dt    = pd.to_datetime(start_r, utc=True).to_pydatetime()
                        end_dt      = pd.to_datetime(end_r,   utc=True).to_pydatetime()
                        window_days = max(
                            (end_dt - start_dt).total_seconds() / 86_400, 1.0
                        )
                    except Exception:
                        pass

                # ── Time remaining in market ──
                time_left_str = "N/A"
                try:
                    end_dt_utc    = pd.to_datetime(end_r, utc=True).to_pydatetime()
                    delta_secs    = (end_dt_utc - datetime.now(timezone.utc)).total_seconds()
                    days_left     = max(delta_secs / 86_400, 0.0)
                    hours_left    = max((delta_secs % 86_400) / 3600, 0.0)
                    time_left_str = f"{int(days_left)}d {int(hours_left)}h remaining"
                except Exception:
                    pass

                count_in_window = posts_in_windows.get(t_id, 0)

                window_pred_str = "N/A"
                wp = wlo = whi = None
                try:
                    pm  = prediction["posterior_mean"]
                    ps  = prediction["posterior_std"]
                    efm = 1.0 + ef

                    now_utc        = datetime.now(timezone.utc)
                    start_dt_utc   = pd.to_datetime(start_r, utc=True).to_pydatetime()
                    end_dt_utc     = pd.to_datetime(end_r,   utc=True).to_pydatetime()

                    elapsed_days   = max(
                        (now_utc - start_dt_utc).total_seconds() / 86_400, 0.0
                    )
                    remaining_days = max(
                        (end_dt_utc - now_utc).total_seconds() / 86_400, 0.0
                    )
                    elapsed_days   = min(elapsed_days, window_days)


                    observed_daily_rate = count_in_window / max(elapsed_days, 0.5)

                    bayesian_daily_rate = (pm / 7.0) * efm


                    elapsed_fraction = min(elapsed_days / max(window_days, 1.0), 1.0)
                    blended_daily    = (
                        elapsed_fraction       * observed_daily_rate +
                        (1.0 - elapsed_fraction) * bayesian_daily_rate
                    )

                    projected_remaining = blended_daily * remaining_days
                    wp  = max(count_in_window, round(
                        count_in_window + projected_remaining
                    ))

                    ws  = float(np.sqrt(
                        (ps ** 2) * (remaining_days / max(window_days, 1.0))
                    ))
                    ws  = ws * (1.0 - elapsed_fraction * 0.5)

                    wlo = max(count_in_window, round(
                        wp - 1.96 * ws
                    ))
                    whi = round(wp + 1.96 * ws)

                    el_d = int(elapsed_days)
                    el_h = int((elapsed_days - el_d) * 24)
                    re_d = int(remaining_days)
                    re_h = int((remaining_days - re_d) * 24)
                    pace_label = (
                        f"elapsed {el_d}d {el_h}h · "
                        f"{re_d}d {re_h}h left · "
                        f"observed rate {observed_daily_rate:.1f}/day"
                    )

                    window_pred_str = (
                        f"**{wp:,}**  [CI: {wlo:,}–{whi:,}]\n"
                        f"_{pace_label}_"
                    )
                except Exception as exc:
                    logger.debug("Window pred error %s: %s", t_id, exc)

                if target and int(target) > 0:
                    pct    = min(
                        round(count_in_window / int(target) * 100, 1), 100.0
                    )
                    filled = int(pct / 10)
                    bar    = "🟩" * filled + "⬜" * (10 - filled)
                    progress_str = (
                        f"{bar}  {pct}%  ({count_in_window:,}/{int(target):,})"
                    )
                    target_str = f"{int(target):,} tweets"
                else:
                    progress_str = (
                        f"{count_in_window:,} tweets so far (no target set)"
                    )
                    target_str = "—"

                bet_str = "N/A — no target or CI available"
                if target and wp is not None and wlo is not None and whi is not None:
                    try:
                        raw_price = (
                            tracking.get("yesPrice")          or
                            tracking.get("yes_price")         or
                            tracking.get("probability")       or
                            (tracking.get("outcomePrices") or [None])[0]
                        )
                        market_yes = float(raw_price) if raw_price is not None else 0.50
                        if market_yes > 1.0:
                            market_yes /= 100.0
                        market_yes = float(np.clip(market_yes, 0.01, 0.99))

                        bet_info = calculate_optimal_bet(
                            predicted_total  = wp,
                            ci_lower         = wlo,
                            ci_upper         = whi,
                            target           = int(target),
                            market_yes_price = market_yes,
                            kelly_fraction   = 0.25,   # quarter-Kelly = conservative
                            bankroll         = 100.0,  # size relative to \$100 bankroll
                        )
                        bet_str = bet_info["recommendation"]
                    except Exception as exc:
                        logger.debug("Bet calc error %s: %s", t_id, exc)
                        bet_str = "⚠️ Could not compute — check market price field"

                market_fields: list[dict] = [
                    {
                        "name":   "🗓️ Window",
                        "value":  window_str,
                        "inline": False,
                    },
                    {
                        "name":   "⏳ Time Remaining",
                        "value":  time_left_str,
                        "inline": True,
                    },
                    {
                        "name":   "🎯 Target",
                        "value":  target_str,
                        "inline": True,
                    },
                    {
                        "name":   "✅ Tweets in Window",
                        "value":  f"**{count_in_window:,}**",
                        "inline": True,
                    },
                    {
                        "name":   "🔮 Window Prediction",
                        "value":  window_pred_str,
                        "inline": False,
                    },
                    {
                        "name":   "📊 Progress",
                        "value":  progress_str,
                        "inline": False,
                    },
                    {
                        "name":   "💰 Optimal Bet Suggestion",
                        "value":  bet_str,
                        "inline": False,
                    },
                ]

                embeds.append({
                    "title":  f"📌 [{idx}/{len(active_trackings)}] {t_title}",
                    "color":  colour,
                    "fields": market_fields[:25],   # Discord hard limit per embed
                })

        else:
            embeds[0]["fields"].append({
                "name":   "📌 Active Tracking Periods",
                "value":  "None currently active",
                "inline": False,
            })

        DISCORD_EMBED_LIMIT = 10

        async def _post_payload(embed_batch: list[dict]) -> None:
            payload = {"embeds": embed_batch}
            try:
                async with self._session.post(
                    self._webhook_url,
                    json=payload,
                    timeout=aiohttp.ClientTimeout(total=10),
                ) as resp:
                    if resp.status in (200, 204):
                        logger.info(
                            "Discord batch sent ✅ (%d embed(s))",
                            len(embed_batch),
                        )
                    else:
                        body = await resp.text()
                        logger.warning(
                            "Discord webhook returned %d: %s",
                            resp.status, body[:300],
                        )
            except asyncio.TimeoutError:
                logger.warning("Discord webhook timed out.")
            except Exception as exc:
                logger.error("Discord webhook error: %s", exc)

        for i in range(0, len(embeds), DISCORD_EMBED_LIMIT):
            batch = embeds[i : i + DISCORD_EMBED_LIMIT]
            await _post_payload(batch)
            if i + DISCORD_EMBED_LIMIT < len(embeds):
                await asyncio.sleep(1.0)

   
# ──────────────────────────────────────────────────────────────────────────────
# XTRACKER REST CLIENT
# ──────────────────────────────────────────────────────────────────────────────

class XTrackerClient:
    def __init__(self, session: aiohttp.ClientSession):
        self._session = session

    async def _get(
        self, path: str, params: Optional[dict] = None
    ) -> dict | list:
        url = f"{XTRACKER_BASE_URL}{path}"
        async with self._session.get(url, params=params) as resp:
            resp.raise_for_status()
            return await resp.json()

    async def get_user(self) -> dict:
        data = await self._get(
            f"/users/{ELON_HANDLE}", {"platform": PLATFORM}
        )
        return data.get("data", data)

    async def get_posts(
        self,
        start_date: Optional[datetime] = None,
        end_date:   Optional[datetime] = None,
    ) -> list:
        params = {"platform": PLATFORM}
        if start_date:
            params["startDate"] = start_date.strftime("%Y-%m-%dT%H:%M:%SZ")
        if end_date:
            params["endDate"]   = end_date.strftime("%Y-%m-%dT%H:%M:%SZ")
        data = await self._get(f"/users/{ELON_HANDLE}/posts", params)
        return data.get("data", []) if isinstance(data, dict) else data

    async def get_all_trackings(self) -> list:
        data = await self._get(
            f"/users/{ELON_HANDLE}/trackings", {"platform": PLATFORM}
        )
        return data.get("data", []) if isinstance(data, dict) else data

    async def get_active_trackings(self) -> list[dict]:
        """
        Return ALL currently active tracking periods for Elon.
        Falls back to manual date filtering if activeOnly param fails.
        """
        try:
            data = await self._get(
                f"/users/{ELON_HANDLE}/trackings",
                {"platform": PLATFORM, "activeOnly": "true"},
            )
            trackings = data.get("data", []) if isinstance(data, dict) else data
            if trackings:
                return trackings
        except Exception as exc:
            logger.warning("activeOnly fetch failed, falling back: %s", exc)

        # Fallback: fetch all and filter by date manually
        try:
            all_trackings = await self.get_all_trackings()
            now    = datetime.now(timezone.utc)
            active = []
            for t in all_trackings:
                start_raw = t.get("startDate")
                end_raw   = t.get("endDate")
                if not start_raw or not end_raw:
                    continue
                start_dt = pd.to_datetime(start_raw, utc=True).to_pydatetime()
                end_dt   = pd.to_datetime(end_raw,   utc=True).to_pydatetime()
                if start_dt <= now <= end_dt:
                    active.append(t)
            return active
        except Exception as exc:
            logger.warning("Could not fetch active trackings: %s", exc)
            return []

    async def get_tracking_stats(self, tracking_id: str) -> dict:
        data = await self._get(
            f"/trackings/{tracking_id}", {"includeStats": "true"}
        )
        return data.get("data", {})

    async def get_total_count_for_range(
        self, start_date: datetime, end_date: datetime
    ) -> int:
        return len(await self.get_posts(start_date, end_date))

    def _tracking_fields(
        tracking:         dict,
        tweets_this_week: int,
        prediction:       dict,
    ) -> list[dict]:
        """
        Returns Discord embed field dicts for one tracking period,
        including a Bayesian predicted-vs-target assessment.
        """
        title     = tracking.get("title", "Unnamed Market")
        start_raw = tracking.get("startDate", "")
        end_raw   = tracking.get("endDate", "")
        target    = tracking.get("target")

        # Date window + time remaining
        window                   = "N/A"
        days_remaining_in_market = None
        if start_raw and end_raw:
            try:
                start_est = (
                    pd.to_datetime(start_raw, utc=True)
                    .tz_convert(EST_TZ)
                    .strftime("%b %d %I:%M %p")
                )
                end_dt  = pd.to_datetime(end_raw, utc=True)
                end_est = end_dt.tz_convert(EST_TZ).strftime("%b %d %I:%M %p EST")
                window  = f"{start_est} → {end_est}"

                now_utc = datetime.now(timezone.utc)
                delta   = (end_dt.to_pydatetime() - now_utc).total_seconds()
                days_remaining_in_market = max(delta / 86400, 0)
            except Exception:
                pass

        # Progress bar vs target
        progress_str        = "N/A"
        predicted_vs_target = "N/A"
        if target:
            try:
                target_int = int(target)
                pct        = min(round(tweets_this_week / target_int * 100, 1), 100)
                filled     = int(pct / 10)
                bar        = "🟩" * filled + "⬜" * (10 - filled)
                progress_str = f"{bar} {pct}%  ({tweets_this_week}/{target_int})"

                predicted = prediction["predicted_weekly_total"]
                ci_low    = prediction["adjusted_ci_95_lower"]
                ci_high   = prediction["adjusted_ci_95_upper"]

                if ci_low > target_int:
                    verdict = "✅ Likely to **EXCEED** target"
                elif ci_high < target_int:
                    verdict = "❌ Likely to **MISS** target"
                elif predicted > target_int:
                    verdict = "🟡 Predicted to **exceed** (but CI overlaps)"
                else:
                    verdict = "🟡 Predicted to **miss** (but CI overlaps)"

                predicted_vs_target = (
                f"{verdict}\n"
                f"Predicted: **{predicted:,}**  "
                f"[CI: {ci_low:,}–{ci_high:,}]  "
                f"vs Target: **{target_int:,}**"
                )
            except Exception:
                pass

        time_left = (
            f"{days_remaining_in_market:.1f} days remaining"
            if days_remaining_in_market is not None
            else "N/A"
    )   

        fields = [
            {
                "name":  f"🏷️  {title}",
                "value": (
                    f"**Window:** {window}\n"
                    f"**Time left:** {time_left}\n"
                    f"**Target:** {target if target else 'N/A'}\n"
                    f"**Progress:** {progress_str}"
                ),
                "inline": False,
            },
            {
                "name":   "🎯 Bayesian Assessment",
                "value":  predicted_vs_target,
                "inline": False,
            },
            {
                "name":   "\u200b",
                "value":  "─────────────────",
                "inline": False,
            },
        ]
        return fields


# ──────────────────────────────────────────────────────────────────────────────
# TWEET PROCESSOR
# ──────────────────────────────────────────────────────────────────────────────

class TweetProcessor:
    def __init__(
        self,
        csv_manager:        CSVManager,
        model:              BayesianTweetForecaster,
        pattern_analyzer:   TemporalPatternAnalyzer,
        news_scanner:       NewsScanner,
        event_analyzer:     EventFactorAnalyzer,
        deviation_detector: DeviationDetector,
        discord_notifier:   DiscordNotifier,
        xtracker_client:    XTrackerClient,
        status_writer:      StatusWriter,
    ):
        self.csv            = csv_manager
        self.model          = model
        self.patterns       = pattern_analyzer
        self.news           = news_scanner
        self.event_analyzer = event_analyzer
        self.deviation      = deviation_detector
        self.discord        = discord_notifier
        self.client         = xtracker_client
        self.status_writer  = status_writer

        self._daily_counts:          dict[int, int]     = defaultdict(int)
        self._current_week_key:      Optional[str]      = None
        self._last_known_cumulative: int                = (
            csv_manager.latest_cumulative_count()
        )
        self._seen_tracking_ids:     set[str]           = set()
        self._news_scan_lock:        asyncio.Lock       = asyncio.Lock()
        self._active_trackings:      list[dict]         = []
        self._tracking_cache_ts:     Optional[datetime] = None
        self._tracking_cache_ttl:    int                = 300

        logger.info(
            "TweetProcessor ready. Cumulative tweets in CSV: %d",
            self._last_known_cumulative,
        )

    async def _get_active_trackings(self) -> list[dict]:
        """Return cached active trackings list; refresh if stale."""
        now = datetime.now(timezone.utc)
        if (
            not self._active_trackings or
            self._tracking_cache_ts is None or
            (now - self._tracking_cache_ts).total_seconds() > self._tracking_cache_ttl
        ):
            self._active_trackings  = await self.client.get_active_trackings()
            self._tracking_cache_ts = now
            logger.info(
                "Active trackings refreshed: %d market(s) found.",
                len(self._active_trackings),
            )
        return self._active_trackings

    async def ingest_posts(self, posts: list[dict]) -> int:
        if not posts:
            return 0

        last_ts   = self.csv.latest_timestamp()
        new_posts = []

        for post in posts:
            raw_ts = (
                post.get("createdAt") or
                post.get("timestamp") or
                post.get("created_at")
            )
            if not raw_ts:
                continue
            ts = pd.to_datetime(raw_ts, utc=True).to_pydatetime()
            if last_ts is None or ts > last_ts:
                new_posts.append((ts, post))

        if not new_posts:
            return 0

        new_posts.sort(key=lambda x: x[0])

        active_trackings = await self._get_active_trackings()

        for ts, _ in new_posts:
            self._last_known_cumulative += 1
            self.csv.append_row(ts, self._last_known_cumulative)

            est_ts   = ts.astimezone(EST_TZ)
            dow      = est_ts.weekday()
            week_key = est_ts.strftime("%Y-W%W")

            if week_key != self._current_week_key:
                logger.info(
                    "New week detected: %s → resetting daily counts & posterior.",
                    week_key,
                )
                self._daily_counts     = defaultdict(int)
                self._current_week_key = week_key
                self.model._reset_week_state()

            self._daily_counts[dow] += 1
            tweets_this_week = sum(self._daily_counts.values())
            prediction       = self.model.update(dow, self._daily_counts[dow])

            deviation_report = self.deviation.evaluate(
                day_of_week    = dow,
                actual_count   = self._daily_counts[dow],
                posterior_mean = prediction["posterior_mean"],
                day_weights    = self.model.day_weights,
            )

            if deviation_report["is_high_deviation"]:
                asyncio.create_task(
                    self._run_news_scan(
                        trigger = (
                            f"deviation_"
                            f"{deviation_report['deviation_direction']}"
                        ),
                        z_score = deviation_report["z_score"],
                    )
                )

    # fetch per-window counts then send Discord notification
        posts_in_windows: dict[str, int] = {}
        if active_trackings:
            try:
                for t in active_trackings:
                    t_id    = t.get("id", "")
                    start_r = t.get("startDate")
                    end_r   = t.get("endDate")
                    if not t_id or not start_r or not end_r:
                        continue
                    try:
                        stats = await self.client.get_tracking_stats(t_id)
                        total = stats.get("stats", {}).get("total")
                        if total is not None:
                            posts_in_windows[t_id] = int(total)
                            continue
                    except Exception:
                        pass
                    try:
                        start_dt = pd.to_datetime(start_r, utc=True).to_pydatetime()
                        end_dt   = pd.to_datetime(end_r,   utc=True).to_pydatetime()
                        posts_in_windows[t_id] = (
                            await self.client.get_total_count_for_range(
                                start_dt, end_dt
                            )
                        )
                    except Exception as exc:
                        logger.warning("Window count error %s: %s", t_id, exc)
                        posts_in_windows[t_id] = 0
            except Exception as exc:
                logger.warning("Could not build posts_in_windows: %s", exc)

        logger.info("Attempting to send discord notification...")
        await self.discord.send(
        new_tweet_count  = 1,
        cumulative_count = self._last_known_cumulative,
        tweets_this_week = tweets_this_week,
        prediction       = prediction,
        active_trackings = active_trackings,
        posts_in_windows = posts_in_windows,
    )   

        self.model.save()

        # Write status.json for the management web app
        self.status_writer.write(
            state            = "running",
            cumulative_count = self._last_known_cumulative,
            tweets_this_week = tweets_this_week,
            prediction       = prediction,
            active_trackings = active_trackings,
            posts_in_windows = posts_in_windows,
            model_info       = {
                "prior_mean":     round(self.model.prior_mean, 2),
                "prior_std":      round(float(np.sqrt(self.model.prior_variance)), 2),
                "training_weeks": len(self.model.training_weeks),
                "event_factor":   round(self.model.event_tracker.current_factor, 4),
            },
        )

        logger.info(
            "Ingested %d new tweet(s). Cumulative: %d",
            len(new_posts), self._last_known_cumulative,
        )
        return len(new_posts)

    async def _run_news_scan(
        self,
        trigger:  str   = "periodic",
        z_score:  float = 0.0,
        lookback: int   = NEWS_LOOKBACK_HOURS,
    ) -> None:
        async with self._news_scan_lock:
            event_logger.info(
                "News scan triggered  source=%-30s  z_score=%+.2f",
                trigger, z_score,
            )
            articles = await self.news.fetch(lookback_hours=lookback)
            analysis = self.event_analyzer.analyze(articles)
            self.model.event_tracker.update(
                analysis["event_factor"], source=trigger
            )
            self.model.save()
            event_logger.info(
                "Post-scan: %s", self.model.event_tracker.summary()
            )

    async def check_and_retrain(self) -> None:
        try:
            trackings = await self.client.get_all_trackings()
        except Exception as exc:
            logger.warning("Could not fetch trackings: %s", exc)
            return

        now_utc = datetime.now(timezone.utc)

        for tracking in trackings:
            tracking_id = tracking.get("id", "")
            if not tracking_id or tracking_id in self._seen_tracking_ids:
                continue

            end_raw = tracking.get("endDate")
            if not end_raw:
                continue

            end_dt    = pd.to_datetime(end_raw, utc=True).to_pydatetime()
            is_active = tracking.get("isActive", True)

            if is_active or end_dt > now_utc:
                continue

            logger.info(
                "Resolved tracking: %s  (ended %s)",
                tracking_id, end_dt.isoformat(),
            )

            try:
                stats = await self.client.get_tracking_stats(tracking_id)
                total = stats.get("stats", {}).get("total")

                if total is None:
                    start_dt = pd.to_datetime(
                        tracking.get("startDate"), utc=True
                    ).to_pydatetime()
                    total = await self.client.get_total_count_for_range(
                        start_dt, end_dt
                    )

                logger.info(
                    "Official total for %s: %d", tracking_id, total
                )
                new_weights = self.patterns.analyze_and_log()
                self.model.retrain(int(total), new_weights)
                self.model.save()
                self._seen_tracking_ids.add(tracking_id)
                self._active_trackings  = []
                self._tracking_cache_ts = None

            except Exception as exc:
                logger.error(
                    "Retraining failed (%s): %s", tracking_id, exc
                )


# ──────────────────────────────────────────────────────────────────────────────
# ASYNC TASK LOOPS
# ──────────────────────────────────────────────────────────────────────────────

async def poll_loop(processor: TweetProcessor) -> None:
    """
    Polls XTracker REST API every POLL_INTERVAL_SEC (5 minutes).
    Fetches all posts since the last recorded timestamp and ingests them.
    """
    logger.info(
        "REST polling loop started — interval: %ds.", POLL_INTERVAL_SEC
    )
    while True:
        try:
            since = processor.csv.latest_timestamp() or (
                datetime.now(timezone.utc) - timedelta(minutes=10)
            )
            posts = await processor.client.get_posts(start_date=since)
            new_n = await processor.ingest_posts(posts)
            if new_n:
                logger.info("Poll: ingested %d new tweet(s).", new_n)
            else:
                logger.info("Poll: no new tweets.")
        except Exception as exc:
            logger.error("Poll error: %s", exc)

        await asyncio.sleep(POLL_INTERVAL_SEC)


async def news_scan_loop(processor: TweetProcessor) -> None:
    """Periodic news scan with EventFactor decay applied before each cycle."""
    logger.info(
        "News scan loop started — interval: %ds.", NEWS_SCAN_INTERVAL
    )
    while True:
        await asyncio.sleep(NEWS_SCAN_INTERVAL)
        processor.model.event_tracker.decay()
        await processor._run_news_scan(trigger="periodic_scheduled")


async def bet_resolution_loop(processor: TweetProcessor) -> None:
    """Checks for resolved tracking periods and retrains the model."""
    logger.info(
        "Bet-resolution monitor started — interval: %ds.", BET_CHECK_INTERVAL
    )
    while True:
        await asyncio.sleep(BET_CHECK_INTERVAL)
        await processor.check_and_retrain()


async def reload_watcher_loop(
    processor:        TweetProcessor,
    csv_manager:      CSVManager,
    pattern_analyzer: TemporalPatternAnalyzer,
) -> None:
    """Watches for a reload.flag file written by the .NET management app.
    When found, reloads the model (.pkl) and CSV, then deletes the flag.
    """
    logger.info("Reload watcher started — checking every 10s for %s.", RELOAD_FLAG_FILE)
    flag_path = Path(RELOAD_FLAG_FILE)
    while True:
        await asyncio.sleep(10)
        if not flag_path.exists():
            continue
        logger.info("Reload flag detected — reloading model and CSV…")
        try:
            flag_path.unlink(missing_ok=True)

            # Re-bootstrap the model from the (possibly updated) .pkl / CSV
            new_model = bootstrap_model(csv_manager, pattern_analyzer)

            # Hot-swap the model inside the processor
            processor.model = new_model
            processor._last_known_cumulative = csv_manager.latest_cumulative_count()
            processor._daily_counts          = defaultdict(int)
            processor._current_week_key      = None
            processor._active_trackings      = []
            processor._tracking_cache_ts     = None

            processor.status_writer.write(
                state            = "reloaded",
                cumulative_count = processor._last_known_cumulative,
                model_info       = {
                    "prior_mean":     round(new_model.prior_mean, 2),
                    "prior_std":      round(float(np.sqrt(new_model.prior_variance)), 2),
                    "training_weeks": len(new_model.training_weeks),
                    "event_factor":   round(new_model.event_tracker.current_factor, 4),
                },
            )
            logger.info("Reload complete — model and CSV refreshed.")
        except Exception as exc:
            logger.error("Reload failed: %s", exc)


async def status_update_loop(processor: TweetProcessor) -> None:
    """Periodically refreshes status.json even when no tweets arrive."""
    while True:
        await asyncio.sleep(60)
        try:
            _json_log_handler.trim()
            processor.status_writer.write(
                state            = "running",
                cumulative_count = processor._last_known_cumulative,
                tweets_this_week = sum(processor._daily_counts.values()),
                model_info       = {
                    "prior_mean":     round(processor.model.prior_mean, 2),
                    "prior_std":      round(float(np.sqrt(processor.model.prior_variance)), 2),
                    "training_weeks": len(processor.model.training_weeks),
                    "event_factor":   round(processor.model.event_tracker.current_factor, 4),
                },
            )
        except Exception:
            pass


# ──────────────────────────────────────────────────────────────────────────────
# MODEL BOOTSTRAP
# ──────────────────────────────────────────────────────────────────────────────

def bootstrap_model(
    csv_manager:      CSVManager,
    pattern_analyzer: TemporalPatternAnalyzer,
) -> BayesianTweetForecaster:

    if Path(MODEL_FILE).exists():
        try:
            model = BayesianTweetForecaster.load(MODEL_FILE)
            logger.info("Resuming from saved model.")
            return model
        except Exception as exc:
            logger.warning(
                "Could not load model (%s) — rebuilding.", exc
            )

    logger.info("Building fresh model from CSV…")
    df = csv_manager.load_dataframe()

    if df.empty:
        logger.info("No CSV data — using default prior (mean=100, std=40).")
        model = BayesianTweetForecaster(prior_mean=100.0, prior_std=40.0)
        model.save()
        return model

    df["DateTime_EST"] = df["DateTime_UTC"].dt.tz_convert(EST_TZ)
    df["DailyCount"]   = (
        df["Cumulative_Tweet_Count"].diff()
        .fillna(df["Cumulative_Tweet_Count"].iloc[0])
        .clip(lower=0)
    )
    df["YearWeek"]  = df["DateTime_EST"].dt.strftime("%Y-W%W")
    weekly_totals   = df.groupby("YearWeek")["DailyCount"].sum()

    prior_mean = (
        float(weekly_totals.mean()) if len(weekly_totals) > 0 else 100.0
    )
    prior_std = max(
        float(weekly_totals.std()) if len(weekly_totals) > 1 else 40.0,
        10.0,
    )
    day_weights = pattern_analyzer.analyze_and_log()

    model = BayesianTweetForecaster(
        prior_mean     = prior_mean,
        prior_std      = prior_std,
        day_weights    = day_weights,
        training_weeks = list(map(int, weekly_totals.tolist())),
    )
    logger.info(
        "Fresh model: %d historical weeks, prior mean=%.1f std=%.1f",
        len(weekly_totals), prior_mean, prior_std,
    )
    model.save()
    return model


# ──────────────────────────────────────────────────────────────────────────────
# GRACEFUL SHUTDOWN
# ──────────────────────────────────────────────────────────────────────────────

def install_shutdown_handler(
    model: BayesianTweetForecaster,
    loop:  asyncio.AbstractEventLoop,
) -> None:
    def _shutdown(sig_name: str) -> None:
        logger.info("Received %s — saving model before exit…", sig_name)
        model.save()
        for task in asyncio.all_tasks(loop):
            task.cancel()

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(
                sig, lambda s=sig.name: _shutdown(s)
            )
        except NotImplementedError:
            pass


# ──────────────────────────────────────────────────────────────────────────────
# MAIN
# ──────────────────────────────────────────────────────────────────────────────

async def main() -> None:
    logger.info("=" * 70)
    logger.info(" Elon Musk Weekly Tweet Predictor — Bayesian + News + Discord")
    logger.info("=" * 70)

    csv_manager        = CSVManager(CSV_FILE)
    pattern_analyzer   = TemporalPatternAnalyzer(csv_manager)
    model              = bootstrap_model(csv_manager, pattern_analyzer)
    deviation_detector = DeviationDetector(csv_manager)
    event_analyzer     = EventFactorAnalyzer()
    status_writer      = StatusWriter(STATUS_FILE)

    connector = aiohttp.TCPConnector(limit=10)
    timeout   = aiohttp.ClientTimeout(total=30)

    async with aiohttp.ClientSession(connector=connector, timeout=timeout) as session:
        xtracker_client  = XTrackerClient(session)
        news_scanner     = NewsScanner(session)
        discord_notifier = DiscordNotifier(session)

        processor = TweetProcessor(
            csv_manager        = csv_manager,
            model              = model,
            pattern_analyzer   = pattern_analyzer,
            news_scanner       = news_scanner,
            event_analyzer     = event_analyzer,
            deviation_detector = deviation_detector,
            discord_notifier   = discord_notifier,
            xtracker_client    = xtracker_client,
            status_writer      = status_writer,
        )

        try:
            user_info = await xtracker_client.get_user()
            logger.info("Tracking: @%s  (%s)",
                        user_info.get("handle", ELON_HANDLE), PLATFORM)
        except Exception as exc:
            logger.warning("Could not fetch user info: %s", exc)

        # Initial backfill
        logger.info("Backfilling from last recorded timestamp…")
        since = csv_manager.latest_timestamp() or (
            datetime.now(timezone.utc) - timedelta(days=7)
        )
        try:
            posts    = await xtracker_client.get_posts(start_date=since)
            ingested = await processor.ingest_posts(posts)
            logger.info("Backfill complete: %d new tweets.", ingested)
        except Exception as exc:
            logger.warning("Backfill failed: %s", exc)

        # Seed EventFactor before going live
        logger.info("Running startup news scan…")
        await processor._run_news_scan(trigger="startup", lookback=24)

        # Check for already-resolved bets
        await processor.check_and_retrain()

        loop = asyncio.get_running_loop()
        install_shutdown_handler(model, loop)

        # Write initial status.json
        status_writer.write(
            state            = "starting",
            cumulative_count = csv_manager.latest_cumulative_count(),
            model_info       = {
                "prior_mean":     round(model.prior_mean, 2),
                "prior_std":      round(float(np.sqrt(model.prior_variance)), 2),
                "training_weeks": len(model.training_weeks),
                "event_factor":   round(model.event_tracker.current_factor, 4),
            },
        )

        # Send startup notification to Discord
        if DISCORD_WEBHOOK_URL:
            try:
                async with session.post(
                    DISCORD_WEBHOOK_URL,
                    json={"embeds": [{
                        "title": "🚀 Tweet Predictor Deployed",
                        "description": "Service is up and running — waiting for tweets.",
                        "color": 0x2ECC71,
                        "timestamp": datetime.now(timezone.utc).isoformat(),
                        "footer": {"text": "Elon Tweet Predictor · Startup"},
                    }]},
                    timeout=aiohttp.ClientTimeout(total=10),
                ) as resp:
                    logger.info("Discord startup notification sent (status=%d).", resp.status)
            except Exception as exc:
                logger.warning("Discord startup notification failed: %s", exc)

        logger.info("All systems go — entering main loops.")
        try:
            await asyncio.gather(
                poll_loop(processor),
                bet_resolution_loop(processor),
                news_scan_loop(processor),
                reload_watcher_loop(processor, csv_manager, pattern_analyzer),
                status_update_loop(processor),
                return_exceptions=True,
            )
        except asyncio.CancelledError:
            logger.info("Shutdown complete.")


if __name__ == "__main__":
    asyncio.run(main())
