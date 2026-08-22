# 86JP 安全 GM 工具

## 架构

Windows 客户端是原生 WPF `WinExe`，不启动控制台、不承载网页，也不包含 SSH、MySQL 用户名或密码。客户端只通过固定证书指纹的 HTTPS 调用独立 GM 服务。GM 服务和游戏服务端分开部署，不修改游戏服务端源码。

将 SSH/数据库密钥放入发给普通账号的 EXE 会让反编译者直接取得服务器权限，因此本实现把密钥限制在云端 `/etc/dfo-gm/dfo-gm.env`（权限 `0600`）中。

## 登录与权限

- 登录使用 `gateway_credentials.password_verifier`，算法与游戏 Gateway 相同：PBKDF2-SHA256。
- 未写入 `gm_account_permissions` 的新账号自动视为 1 级。
- 1 级：本人角色；仅装备发放和点券。装备参数由服务端重建为默认值，客户端提交强化、增幅或锻造参数不会生效。
- 2 级：可调用 GM 业务功能，但账号 ID、角色 ID 和查询参数均受本人归属校验；全服迁移、全服异常清理、跨账号恢复/克隆被拒绝。
- 3 级：可跨账号、设置权限，并检索安全审计、物品/消费审计和服务端日志。

## 本地 PVF 物品目录

- 发放页首次选择包含 `Script.pvf` 的文件夹，并可单独选择 `ImagePacks2`；路径只保存在本机 `%LocalAppData%\86JP\GM\settings.json`。
- 客户端使用轻量 `GmPvfLib` 解析装备和堆叠物，流程与图标格式参考 `pvfUtility`，不会把 DevExpress 等编辑器依赖带入发布件。
- 压缩索引缓存位于 `%LocalAppData%\86JP\GM\pvf-index-v1.json.gz`，按 PVF 完整路径、大小和最后修改时间自动失效。
- 本地索引仅用于浏览和选择。发放时只提交模板 ID；服务端仍执行物品合法性、权限等级和角色归属验证。
- 1 级客户端仅公开装备分类；2、3 级公开装备和消耗品分类。

会话只保存在服务内存中，有效期 2 小时；客户端不保存游戏密码或会话令牌。登录连续失败会按来源与账号限速。

## 构建

```powershell
dotnet publish DesktopClient/86JPGmClient.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/client

dotnet publish DfoGmTool.csproj -c Release -r linux-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/server-selfcontained
```

## 部署安全要求

- 仅在云端创建 `dfo-gm.env`，不要提交真实连接串、证书密码或管理员账号。
- HTTPS 证书轮换后必须同步更新客户端固定指纹并重新发布。
- API 根路径不提供网页；未认证业务请求返回 401。
- 日志查询结果仅对 3 级账号开放，所有写操作写入 `gm_security_audit`。
