# LK Deploy Guide: Target a Different Agent

This fork runs in no-auth mode (`DISABLE_AUTH=true`) and does **not** require Entra app registration.

Use this guide to point the deployed app at a different Microsoft Foundry agent.

## Quick Decision

- Same Foundry project, different agent name: update `AI_AGENT_ID` then run `azd provision`.
- Different Foundry project endpoint (same or different resource): update `AI_AGENT_ENDPOINT` and `AI_AGENT_ID`, then run `azd provision`.
- Code changes to ship after retargeting: run `azd deploy`.

Why `azd provision`? The agent endpoint/id are injected into Container App environment variables by Bicep, so infrastructure config must be re-applied.

## Prereqs

From repo root:

```bash
cd /home/lkearney/teach-1
azd auth login
azd env list
azd env select <your-env-name>
```

## Option A: Switch to Another Agent in the Same Project

1. List agents in the current project:

```bash
pwsh ./deployment/scripts/list-agents.ps1
```

2. Set the new agent name:

```bash
azd env set AI_AGENT_ID <agent-name>
```

3. Apply config to Container App:

```bash
azd provision
```

4. Validate:

```bash
azd env get-value AI_AGENT_ID
az containerapp show \
  --name "$(azd env get-value AZURE_CONTAINER_APP_NAME)" \
  --resource-group "$(azd env get-value AZURE_RESOURCE_GROUP_NAME)" \
  --query "properties.template.containers[0].env[?name=='AI_AGENT_ID'].value" -o tsv
```

## Option B: Switch to a Different Project/Resource

1. Set project endpoint and target agent:

```bash
azd env set AI_AGENT_ENDPOINT "https://<resource>.services.ai.azure.com/api/projects/<project>"
azd env set AI_AGENT_ID <agent-name>
```

2. If changing to a different Foundry resource, set these so RBAC is updated correctly:

```bash
azd env set AI_FOUNDRY_RESOURCE_GROUP <resource-group>
azd env set AI_FOUNDRY_RESOURCE_NAME <resource-name>
```

3. Re-apply infra config and RBAC:

```bash
azd provision
```

4. Validate endpoint and agent:

```bash
azd env get-value AI_AGENT_ENDPOINT
azd env get-value AI_AGENT_ID
```

## Deploy Updated Code (Optional)

If you also changed code, deploy image update after retargeting:

```bash
azd deploy
```

## Customize Title and Intro Messages

Use this section to change:
- Browser/page title
- Welcome text shown above the prompt cards
- Intro starter prompts like "How can you help me?", "What are your capabilities?", and "Tell me about yourself"

### 1) Browser tab title

- Static HTML fallback title: `frontend/index.html` (`<title>Octoagent-teach</title>`)
- Runtime title (set after agent metadata loads): `frontend/src/App.tsx`
  - Current behavior sets:
    - `<agent name> - Azure AI Agent` when metadata exists
    - `Azure AI Agent` on fallback/error

If you want a custom brand, update both `frontend/index.html` and the `document.title = ...` lines in `frontend/src/App.tsx`.

### 2) Welcome line + default starter prompts

Edit `frontend/src/components/chat/StarterMessages.tsx`:

- Welcome text:
  - Current: `Hello! I'm ${agentName}`
  - Fallback: `Hello! How can I help you today?`
- Default starter prompt list (`defaultStarterPrompts`):
  - `How can you help me?`
  - `What are your capabilities?`
  - `Tell me about yourself`

### 3) Why your edits might not show in production

`StarterMessages.tsx` uses this logic:
- If `starterPrompts` are provided by agent metadata, it uses those.
- Otherwise it uses `defaultStarterPrompts` from the file.

So if your Foundry agent defines starter prompts, those override the local defaults.

### 4) Where agent-provided prompts come from

- Frontend passes metadata prompts from `frontend/src/App.tsx` into `AgentChat` via `starterPrompts={agentMetadata.starterPrompts}`.
- In practice, update prompts in your Foundry agent configuration if you want the hosted agent metadata to drive these cards.

### 5) Deploy content/UI text changes

After editing frontend files:

```bash
azd deploy
```

Local check before deploy:

```bash
cd frontend
npm run dev
```

## One-Command Retarget + Deploy (Common)

```bash
azd env set AI_AGENT_ENDPOINT "https://<resource>.services.ai.azure.com/api/projects/<project>" && \
azd env set AI_AGENT_ID <agent-name> && \
azd env set AI_FOUNDRY_RESOURCE_GROUP <resource-group> && \
azd env set AI_FOUNDRY_RESOURCE_NAME <resource-name> && \
azd provision && \
azd deploy
```

## Troubleshooting

- `401 PermissionDenied` after switching resources:
  - Run `azd provision` again to refresh RBAC role assignments.
  - Restart latest revision if needed:

```bash
REVISION=$(az containerapp revision list \
  --name "$(azd env get-value AZURE_CONTAINER_APP_NAME)" \
  --resource-group "$(azd env get-value AZURE_RESOURCE_GROUP_NAME)" \
  --query "[0].name" -o tsv)
az containerapp revision restart \
  --name "$(azd env get-value AZURE_CONTAINER_APP_NAME)" \
  --resource-group "$(azd env get-value AZURE_RESOURCE_GROUP_NAME)" \
  --revision "$REVISION"
```

- `docker push` issues in Cloud Shell:
  - This repo already falls back to ACR cloud build in Cloud Shell. Use `azd deploy` directly.
