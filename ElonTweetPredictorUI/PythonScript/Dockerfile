FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY elon_must_tweet_predictorv4.py .

RUN mkdir -p /app/data

STOPSIGNAL SIGTERM

CMD ["python", "-u", "elon_must_tweet_predictorv4.py"]
