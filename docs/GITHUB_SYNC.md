# GitHub 源码同步

2026-11-07，用户明确选择 `keyouchen16-hub / AFMC-FIND` 新建私有仓库。

目标：https://github.com/keyouchen16-hub/AFMC-FIND

连接器账号不同，因此通过本机已有 Git Credential Manager 登录执行；凭据仅在内存使用，没有输出、写入项目或嵌入 remote URL。

上传范围：产品源码、最终品牌素材、构建/检查脚本、历史预览源码、人工整理的项目状态和验收报告。排除 `.tools`、`artifacts`、`bin/obj`、个人配置、密钥文件、下载资源和 `docs/evidence` 原始抓取。初次整理约 4.5 MB；5 个带凭据的 URL 是经逐条核对的安全拒绝测试假数据。

仓库是当前开发快照，包含未发布的分片实验工具，不创建 Release、不改版本、不因此安装。正式发布仍需实际起播与卡顿改善后验证。

当前状态：私有仓库已创建，准备首次提交与远端校验。所有者与私有属性已通过 GitHub API 确认。
