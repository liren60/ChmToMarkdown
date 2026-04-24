# Copilot Instructions

## 项目指南
- 编码行为准则（来自 CLAUDE.md）：1. 编码前先思考，明确说明假设，不确定时先问。2. 简洁优先，只实现被要求的功能，不写推测性代码，不做不必要的抽象。3. 外科手术式修改，只改必须改的，不"顺手优化"无关代码，保持现有风格。4. 目标驱动执行，多步骤任务先列计划再实施，用可验证的成功标准衡量完成。
- 当需要创建或修改文件内容时，优先使用 `create_file`、`replace_string_in_file`、`multi_replace_string_in_file` 等工具，而不是 PowerShell 命令（如 Set-Content、Out-File 等）。PowerShell 写入中文内容容易产生编码问题，工具写入更可靠。只在查询文件结构、移动文件等非写入场景才使用命令行。
- C# 项目的程序集名（AssemblyName）、根命名空间（RootNamespace）以及所有源文件中的 namespace 声明，必须使用英文，不能含中文字符，避免路径和反射问题。