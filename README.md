# 隐文匣（Mysterious Characters）

隐文匣是一个 Windows 本地托盘小程序。用户在任意普通输入框中选中文字后，按下全局快捷键，即可把原选区替换为经过本地词典转换的文字。

## 功能

- 默认快捷键：`Ctrl + Alt + E`
- 托盘菜单：开启或暂停、打开设置、退出程序
- 智能混合策略：固定 100% 替换，优先增删偏旁，其次使用同音字和形近字
- 一级常用字覆盖：内置 3500 个常用汉字规则，其中绝大多数包含可验证的偏旁增删候选
- 真实汉字输出：所有候选都必须是单个可显示汉字，无法可靠转换的字保持原样
- 剪贴板保护：转换前保存剪贴板，粘贴后按设置延迟恢复
- 黑名单：按前台进程名跳过敏感或不兼容应用
- 自定义词典：导入本地 JSON 文件，与内置规则合并使用
- 单实例运行：重复启动时提示已有后台实例
- 完全本地：不上传用户输入，不依赖服务器

## 使用

1. 启动 `MysteriousCharacters.exe`。程序会驻留在系统托盘。
2. 在普通文本输入框中选中一段文字。
3. 按 `Ctrl + Alt + E`。
4. 需要修改配置时，双击托盘图标或右键选择“打开设置”。

程序无法保证在管理员权限窗口、安全输入框、密码框、游戏窗口或特殊防注入软件中生效。遇到这些情况时会跳过处理或显示轻量提示。

## 构建

```powershell
dotnet build .\MysteriousCharacters.slnx --configuration Release
```

## 发布单文件 exe

```powershell
.\publish.ps1
```

发布结果位于 `artifacts\win-x64\MysteriousCharacters.exe`。发布配置为 `win-x64`、自包含、单文件，目标 Windows 用户无需预装 .NET 运行时。

## 冒烟测试

```powershell
dotnet run --project .\MysteriousCharacters.SmokeTests -- .\examples\custom-dictionary.example.json .\data\level1-common-characters.txt
```

## 本地数据

设置和导入后的自定义词典位于：

```text
%LocalAppData%\MysteriousCharacters
```

内置精细词典、偏旁家族库和 3500 个一级常用字生成规则作为程序集资源打包进 exe。一级常用字规则优先依据 IDS 顶层拆字关系生成偏旁增删候选：简单字优先增加偏旁，复杂字仅在删除常见偏旁后仍保留可辨认主体时才允许自动减偏旁。随后使用 Unicode 普通话读音和部首笔画数据补充同音、形近候选，形近字不会比原字更简单。无法可靠转换的非常用字保持原样。自定义词典格式可参考 `examples\custom-dictionary.example.json`。每条规则包含一个原字和若干候选字，原字和候选字都必须是单个真实汉字。候选类型可使用：

```text
Homophone
AddRadical
RemoveRadical
Similar
```

候选类型的优先级固定为：先在 `AddRadical` 和 `RemoveRadical` 中选择，再考虑 `Homophone`，最后考虑 `Similar`。同一优先级内，`weight` 必须为正整数，值越大，被随机选中的概率越高。

## 重新生成常用字规则

开发阶段可以使用 `tools\generate_common_character_rules.py` 重新生成常用字规则。仓库中的输入字表为 `data\level1-common-characters.txt`。脚本还需要 CJKVI IDS 数据和 Unicode Unihan 数据，输出为离线 JSON 词典与覆盖率报告。程序运行时不会联网。

当前生成结果见 `docs\common-character-coverage.json`。第三方数据来源与许可说明见 `THIRD_PARTY_NOTICES.md`。
