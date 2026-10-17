# WebGL 试玩与发布

试玩地址：https://bishop-zh.github.io/who-will-boom/

网页只提供单机 / 三名 AI 试玩：编译时禁用 TCP/UDP 房间发现及麦克风语音。Windows 桌面构建仍保留原联机与语音功能。

## 构建

使用 Unity 2022.3.62f3c1，安装 WebGL Build Support。批处理入口为 Assets/Editor/PortfolioWebGLBuild.cs 中的 PortfolioWebGLBuild.Build。

传入 -buildTarget WebGL -executeMethod PortfolioWebGLBuild.Build -portfolioOutput <输出目录>，并指定 -projectPath <本项目目录>、-batchmode -quit 和 -logFile <日志路径>。

构建读取 Build Settings 中启用的场景，使用 gzip 压缩及解压回退。无需服务端进程或自定义 Content-Encoding 响应头。请通过 HTTP 服务访问输出，不要双击本地 HTML。

## 部署结构

- main：Unity 源码和构建入口，遵守 .gitignore。
- gh-pages：仅包含 WebGL 静态输出、试玩外壳和 .nojekyll，由本仓库 Pages 从根目录发布。
- 个人主页只有截图和超链接，访问主页不会下载本项目的游戏二进制。

重新构建后，将完整输出同步到 gh-pages 分支，保留试玩页面外壳及其资源引用，再提交并推送。不要只替换 wasm 而遗漏对应 data / framework / loader 文件。每次发布前确认 build-report.txt 显示 Succeeded，并在浏览器进入实际玩法。

## 试玩页面

页面提供加载进度、错误提示、全屏、源码和返回作品集入口。首次进入需点击开始加载以启用浏览器音频；手机端暂未提供专门的触摸操作适配。浏览器存档只保存在当前站点、当前浏览器，不与桌面版共享。
