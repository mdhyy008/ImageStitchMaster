# X图片拼接

简体中文 | [English](README.en.md)

Windows 图片拼接工具，基于 .NET 8 + WinForms，单文件绿色版，免安装。

## 功能特性

- 横向 / 竖向拼接多张图片，画布尺寸自动计算
- 文件对话框选择 + 拖拽导入，支持 JPG / PNG / BMP，异步批量导入并显示进度
- 图片顺序列表：拖拽排序，支持上移 / 下移 / 删除 / 清空，序号 + 缩略图展示
- 实时预览（后台线程渲染，内存友好）：滚轮缩放、左键拖拽平移、双击复位
- 体积预测：按图片内容复杂度预估拼接结果大小，实时显示在状态栏
- 输出体积上限设置：超限自动压缩（先降 JPG 质量，仍超限再缩小分辨率），PNG 超限自动转 JPG
- 渲染模式：普通（串行，内存友好）/ 并行（多核解码，更快）
- 状态栏显示渲染核心 / 线程数、实时进程内存占用
- 高 DPI 适配（PerMonitorV2）

## 使用方法

1. 在 [Releases](https://github.com/mdhyy008/ImageStitchMaster/releases) 下载最新版本的 `*-win-x64.zip`。
2. 解压，运行 `ImageStitchMaster.exe`（无需安装 .NET 运行时）。
3. 点击「添加图片」或直接拖拽图片到窗口。
4. 在左侧列表拖拽调整图片顺序，选择拼接方向，按需设置「输出体积上限」（单位 MB，留空不限制）。
5. 点击「保存拼接图」，先预览预计体积，确认后选择保存位置与格式。

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
