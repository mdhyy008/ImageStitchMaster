# X图片拼接

Windows 图片拼接工具，基于 .NET 8 + WinForms。支持横向 / 竖向拼接多张图片，可限制输出体积并自动压缩。

## 功能特性

- 横向 / 竖向拼接多张图片，画布尺寸自动计算
- 文件对话框选择 + 拖拽导入，支持 JPG / PNG / BMP
- 图片顺序列表管理（上移 / 下移 / 删除 / 清空）
- 实时缩略图预览（后台线程，低内存）
- 输出体积上限设置，超限自动压缩（质量优先，仍超限再缩小分辨率）
- 输出格式 PNG / JPG（JPG 质量可自动调节）
- 高 DPI 适配，单文件绿色版

## 使用方法

1. 在 [Releases](https://github.com/mdhyy008/ImageStitchMaster/releases) 下载最新版本的 `*-win-x64.zip`。
2. 解压，运行 `ImageStitchMaster.exe`（无需安装 .NET 运行时）。
3. 点击「添加图片」或直接拖拽图片到窗口。
4. 选择拼接方向，按需设置「输出体积上限」（单位 MB，留空表示不限制）。
5. 点击「保存拼接图」，选择保存位置与格式。

## 从源码构建

```bash
dotnet build -c Release
```

发布为 Windows x64 单文件（无需安装 .NET 运行时）：

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## 许可证

[MIT](LICENSE)
