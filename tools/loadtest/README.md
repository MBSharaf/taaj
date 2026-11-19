# Load test tool (اختبار التحمل)

Seeds 5,000 doctors + 5,000 labs + one open case per pair, signs them all in, then runs:

| command | what it does |
|---|---|
| `seed` | creates the users/labs/cases directly in the database (`--cs "<connection string>"`) |
| `login` | signs in all 10,000 users (`--concurrency 100`), saves session cookies |
| `s1` | 1,000 doctors create a new case at the same moment |
| `s2` | 10,000 live chat connections; 5 rounds where all 5,000 doctors send at once and labs reply; then steady chat (`--interval 20` s per user). Use `--shards 2 --shard 0/1 --t0 <unix time>` to split across two processes, then `merge --name s2` |
| `s3` | 10,000 users upload a file (`--kb 1024`) and post it in the case chat at the same moment |

Run **only against a local/test database**, never production. Every user gets its own `X-Forwarded-For`
address so the per-address rate limit behaves like 10,000 different people (the API must see the tool as a
trusted proxy, i.e. run it on the same machine). Results go to `--out` (default `/work/results`).

```
dotnet run -c Release -- seed --cs "Server=127.0.0.1,1433;Database=TaajLoad;User Id=sa;Password=...;TrustServerCertificate=True"
dotnet run -c Release -- login --url http://127.0.0.1:5080
dotnet run -c Release -- s1
```
