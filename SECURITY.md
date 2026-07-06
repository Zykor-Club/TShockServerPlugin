# 安全策略 | Security Policy

## 报告安全漏洞 | Reporting a Vulnerability

如果你发现了安全漏洞，**请不要公开提交 Issue**。

请通过以下方式私下报告：

- 发送邮件至：1011819146@qq.com
- 或联系仓库维护者

我们会尽快回复并修复问题。

## 安全最佳实践

- 插件应使用 TShock 权限系统（`[Permission]`）控制命令访问
- 避免在插件中硬编码敏感信息（密码、Token 等）
- 使用 `Commands.HandleCommand(TSPlayer.Server, ...)` 时注意安全风险
- 所有用户输入应进行验证和清洗
