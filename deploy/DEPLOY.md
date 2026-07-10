# Auto-deploy: GitHub Actions → SSH → `docker compose`

On every push to `main`, `.github/workflows/deploy.yml` rsyncs the repo to the
**cloud** box, runs `docker compose up -d --build`, and gates on the container's
Docker healthcheck (`/healthz`). Builds happen **on the box** (8 GB RAM — fine for an
occasional .NET build; switch to the GHCR-image variant if it ever gets tight).

## One-time setup

### 1. Deploy SSH key
A dedicated key just for deploys (don't reuse a personal key).
```bash
ssh-keygen -t ed25519 -f ~/.ssh/toggl_mcp_deploy -N "" -C "toggl-mcp-deploy"
```
Add the **public** key to the deploy user's authorized_keys on cloud:
```bash
ssh-copy-id -i ~/.ssh/toggl_mcp_deploy.pub <user>@204.168.233.148
# or append ~/.ssh/toggl_mcp_deploy.pub to /home/<user>/.ssh/authorized_keys manually
```
The deploy user must be able to run `docker compose` (in the `docker` group or root).

### 2. Cloud checkout dir + `.env`
```bash
ssh <user>@204.168.233.148 'sudo mkdir -p /opt/toggl-mcp && sudo chown $USER /opt/toggl-mcp'
```
Create `/opt/toggl-mcp/.env` on the box (NOT in git — rsync preserves it):
```env
TOGGL__APITOKEN=<your-toggl-token>
AUTH__ENABLED=true
AUTH__AUTHORITY=https://auth.irminsul.uk/application/o/toggl-mcp/
AUTH__RESOURCEURL=https://toggl.irminsul.uk/mcp
STORAGE__DATADIR=/data
ASPNETCORE_URLS=http://+:8080
```
Ensure the shared proxy network exists (it already does on cloud): `docker network create web` (no-op if present).

### 3. Repo secrets
Set on `PetrValik/toggl-mcp` (Settings → Secrets → Actions), or via `gh`:
```bash
gh secret set SSH_HOST         --body "204.168.233.148"
gh secret set SSH_USER         --body "<deploy-user>"
gh secret set SSH_KEY          < ~/.ssh/toggl_mcp_deploy          # the PRIVATE key
ssh-keyscan -H 204.168.233.148 | gh secret set SSH_KNOWN_HOSTS    # pins the host key
# optional, only if SSH is not on 22:
# gh secret set SSH_PORT --body "<port>"
```

### 4. First deploy
Push to `main` (or run the workflow manually: `gh workflow run deploy.yml`), then watch:
```bash
gh run watch
```
The job fails loudly if `/opt/toggl-mcp/.env` is missing or the container never turns healthy.

### 5. Caddy (once)
Add `deploy/Caddyfile.snippet` to the cloud Caddyfile and reload — see that file.
Then `curl https://toggl.irminsul.uk/healthz`.

## Notes
- rsync uses `--delete` but **excludes** `.env`, `data/`, `.git/`, `bin/`, `obj/`, so cloud-only
  state (your token, the token vault / mapping rules volume) is never wiped.
- The GitHub runner reaches cloud over the **public IP** (it's not on your tailnet), so SSH
  must be reachable there. Lock it down with a firewall + key-only auth as usual.
- To roll back: `git revert` + push, or on the box `git`-less — just re-run an older workflow.
