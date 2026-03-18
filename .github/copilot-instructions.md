# Copilot Instructions

## Project Guidelines
- For this project, the deployment pipeline should deploy only the .NET UI container; the Python predictor is deployed separately. 
- The UI must read status, log, and model-related files from the shared data volume mounted from the separate Python container, not local-only assumptions. The UI must still mount the shared data volume to read predictor logs.