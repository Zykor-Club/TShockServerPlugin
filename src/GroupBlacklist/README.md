# GroupBlacklist 组黑名单

- 作者: 星梦
- 出处: `https://github.com/Zykor-Club/TShockServerPlugin`
- 禁止指定用户组进入服务器，支持在线踢出和豁免名单
- 支持 `/gb` 指令动态管理，无需手动编辑配置文件

## 指令

| 语法 | 权限 | 说明 |
|-----|:----:|------|
| `/gb` | `groupblacklist.admin` | 显示帮助信息 |
| `/gb on` | `groupblacklist.admin` | 开启插件功能 |
| `/gb off` | `groupblacklist.admin` | 关闭插件功能 |
| `/gb add <组名>` | `groupblacklist.admin` | 添加黑名单组 |
| `/gb del <组名>` | `groupblacklist.admin` | 移除黑名单组 |
| `/gb padd <玩家>` | `groupblacklist.admin` | 添加豁免玩家 |
| `/gb pdel <玩家>` | `groupblacklist.admin` | 移除豁免玩家 |
| `/gb list` | `groupblacklist.admin` | 查看当前列表和状态 |
| `/reload` | `tshock.cfg.reload` | 重载配置文件 |

## 配置
> 配置文件路径：tshock/GroupBlacklist.json

```json5
{
  "插件设置": {
    "启用插件": true,
    "拒绝加入提示信息": "你的用户组被禁止进入此服务器",
    "踢出提示信息": "你所属的用户组已被列入黑名单",
    "检测间隔(秒)": 10,
    "是否踢出在线黑名单玩家": true,
    "是否记录日志": true
  },
  "黑名单组列表": ["poooo", "如悠"],
  "豁免玩家列表": []
}
```

## 更新日志

### v1.1.0 (2026-08-12)
- 新增 `/gb` 指令集，支持动态管理黑名单和豁免名单
- 新增 `启用插件` 配置项，支持 /gb on/off 开关
- 新增 `/gb list` 查看当前状态


### v1.0.0 (2026-04-05)
- 初始版本发布

## 反馈
优先发 Issue -> 共同维护的插件库：`https://github.com/Zykor-Club/TShockServerPlugin`
次优先：TShock官方群：816771079