# 贡献指南 | Contributing Guide

感谢你愿意为本仓库贡献代码！🎉

## 插件添加流程

### 1. Fork 仓库
如果你不是组织成员，请先 Fork 本仓库。

### 2. 创建分支
```bash
git checkout -b add-my-plugin
```

### 3. 插件结构要求
每个插件必须遵循以下目录结构：

```
src/你的插件名/
├── 你的插件名.csproj          # 必须导入 template.targets
├── *.cs                       # 源码
├── manifest.json              # 插件描述（多语言）
├── README.md                  # 中文文档（必须包含使用说明和命令列表）
└── README.en-US.md           # 英文文档（可选但推荐）
```

#### .csproj 模板
```xml
<Project Sdk="Microsoft.NET.Sdk">
    <Import Project="..\..\template.targets" />
    <!-- 额外 NuGet 包在此添加 -->
</Project>
```

#### manifest.json 模板
```json
{
    "README": {
        "Description": "插件的中文描述"
    },
    "README.en-US": {
        "Description": "Plugin English description"
    }
}
```

### 4. 更新解决方案
将新插件添加到 `Plugin.slnx`：

```xml
<Project Path="src/你的插件名/你的插件名.csproj" />
```

### 5. 提交 PR
提交 Pull Request 时，请确保：
- ✅ 项目已加入 Plugin.slnx
- ✅ .csproj 已导入 `template.targets`
- ✅ 已添加 manifest.json
- ✅ 已添加 README.md（含完整命令说明）
- ✅ 插件可正常编译 (`dotnet build Plugin.slnx`)
- ❌ 不要提交 `bin/`、`obj/`、`out/` 目录
- ❌ 不要提交 `*.dll`、`*.pdb`、`*.exe` 文件

## 插件更新流程

1. 修改版本号
2. 更新 README.md 更新日志
3. 提交 PR

## 编码规范

- 目标框架：.NET 9.0
- 语言版本：C# 12+
- TShock 版本：6.1.0 (Terraria 1.4.5.6)
- 遵循 `.editorconfig` 中的代码风格
- 插件类应继承 `TerrariaPlugin` 基类
- 使用 `GetDataHandlers` 或 `Hooks` 注册事件（而非 OTAPI 直接 Hook）
- 避免使用硬编码路径引用 TShock 或 Terraria 程序集
