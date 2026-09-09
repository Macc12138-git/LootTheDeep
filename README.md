# LootTheDeep

## Git 与 Git LFS

- 提交 `Assets/`（包含 `.meta`）、`Packages/`、`ProjectSettings/` 和仓库配置文件。
- `.gitignore` 排除 Unity 缓存、临时文件、日志、构建产物及本机 IDE 配置。
- `.gitattributes` 将图片、音频、视频、模型、字体、二进制插件和压缩包交给 Git LFS，按扩展名匹配，不按大小判断。
- 场景、预制体、`.meta` 等文本文件保留普通 Git 管理；`.asset` 不统一加入 LFS，以免把文本配置也转为 LFS 指针。后续遇到二进制 `.asset` 时按具体路径添加规则。

### 新机器初始化

先安装 Git LFS，再执行：

```sh
git clone https://github.com/Macc12138-git/LootTheDeep.git
cd LootTheDeep
git lfs install --local
git lfs pull
```

如果已有本地仓库，只需在仓库目录执行后两条命令。LFS 安装和 hook 是本机设置，每个新克隆均需初始化。

### 检查与新增规则

```sh
git lfs status
git lfs ls-files
git check-attr filter -- Assets/example.png
git lfs track "Assets/path/to/large-binary.asset"
```

新增规则后需一并提交 `.gitattributes` 和对应资源。远端拉取、推送需要有效的 GitHub 凭据；本地 LFS 初始化成功不代表远端认证或 LFS 上传已验证。
