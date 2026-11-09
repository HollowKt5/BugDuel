# 出站访问申请：opencode.ai（HTTPS 443）

提交人：__________　部门：__________　日期：__________
终端：Windows 工作站，内网 IP `192.168.12.49`，网关 `192.168.12.2`，DNS `192.168.2.154`

## 一、申请事项

请为上述终端放通到 **`opencode.ai`（含 `*.opencode.ai`）TCP 443 出站**访问，用于 AI 编程助手的模型 API 调用。

- 目标域名：`opencode.ai`
- 协议/端口：HTTPS（TLS 1.2 / 1.3），TCP 443，HTTP/1.1 + SSE 流式响应
- 实际访问路径：`https://opencode.ai/zen/go/v1`（模型推理网关）
- 该域名解析到 Cloudflare 边缘节点，当前为 `172.65.90.20 / .21 / .23`，**IP 会漂移，建议按域名（SNI）放行而不是按 IP**

## 二、用途与流量特征

- 用途：开发工具调用大模型 API，收发 JSON 请求与流式文本响应。
- 单次请求体通常几十 KB 至数 MB（含图片时），响应为逐步下发的 SSE 流，单连接可持续数十秒至数分钟。
- 无 P2P、无大文件下载、无对外提供服务，仅客户端主动出站。

## 三、当前故障现象

该域名目前**无法建立 TLS 连接**，表现为连接被立即重置：

| 检查项 | 结果 |
|---|---|
| DNS 解析 `opencode.ai` | 正常，返回 `172.65.90.20/21/23`（真实 Cloudflare 地址，无污染） |
| TCP 连接 `opencode.ai:443` | 成功 |
| TLS 握手 | **ClientHello 发出后 4–21 ms 内被 RST（ECONNRESET）**，收不到任何证书 |

对照测试（同一出口、同一时间段）：

| 目标 | 结果 |
|---|---|
| `https://github.com` | 正常（HTTP 200） |
| `https://api.deepseek.com` | 正常（401，未带密钥，网络通） |
| `https://api.openai.com` | 正常（401） |
| `https://www.cloudflare.com` | 正常（200） |
| `https://www.baidu.com` | 秒断（ECONNRESET） |
| `https://registry.npmjs.org` | 秒断（ECONNRESET） |
| **`https://opencode.ai`** | **秒断（ECONNRESET）** |

关键判据：**同一个 IP，换 SNI 就能握手成功**

- `openssl s_client -connect 172.65.90.20:443 -servername opencode.ai` → 握手被重置
- `openssl s_client -connect 172.65.90.20:443 -servername cloudflare.com` → TLS 1.3 握手成功

说明该 IP 本身可达，是**按域名（SNI）进行的放行/阻断策略**把 `opencode.ai` 拦掉了，而不是路由不可达或 DNS 问题。同样现象也出现在 `www.baidu.com`、`registry.npmjs.org` 上，判断出口侧存在域名级白名单策略。

## 四、希望的处置方式（任一即可）

1. **首选：按域名放行**
   在网络出口策略中将 `opencode.ai`（建议含 `*.opencode.ai`）加入 HTTPS 出站允许列表，不做 SNI 阻断。

2. **备选：提供 HTTP 代理**
   若必须经代理出网，请提供一个 **`http://` 形式的代理地址与端口**（含认证方式）。
   注意：本客户端**只支持 HTTP/HTTPS 代理，不支持 SOCKS 与 PAC**，请勿提供 `socks5://` 地址。

3. **如涉及 TLS 解密审计**
   请提供内部根证书（PEM/CRT 文件）。我们会通过 `SSL_CERT_FILE` 指向该证书链，否则客户端会因证书校验失败报错。

## 五、可提供的配合信息

- 需要抓包或日志定位时，可提供源 IP、时间点与失败时刻（示例：`2026-11-05 15:54:02`，连续 5 次连接被重置）。
- 如需要，可在终端上现场执行上述 `openssl` 对照命令复现。

## 六、给网络同事的复现命令

```bash
# 1) 预期失败：握手被重置
curl -v --max-time 10 https://opencode.ai/zen/go

# 2) 预期失败：仅指定 SNI
openssl s_client -connect opencode.ai:443 -servername opencode.ai </dev/null

# 3) 预期成功：同一 IP，换 SNI —— 证明是域名策略而非 IP 不通
openssl s_client -connect 172.65.90.20:443 -servername cloudflare.com </dev/null
```

## 附：一句话说明（口头沟通用）

> 我们有个开发用的 AI 工具，它的 API 域名 `opencode.ai` 被出口策略挡了——TCP 能连上，但 TLS 握手一发就被重置；同一个 Cloudflare IP 换成别的域名就能通，说明是按域名做的策略。能不能把这个域名加进 HTTPS 出站白名单？IP 是 Cloudflare 的会变，最好按域名放行。
