[中文](#中文) | [English](#english) | [日本語](#日本語)

---

<h1 id="中文">如何创建.neko7z</h1>

Neko7z 是由 Starfelll 为 NekoVpk 发明的一种特殊内嵌格式(其实就是重命名的压缩包)，允许在同一个 VPK 文件中打包多个变体（Variants）。
**⚠️ 核心机制警告：** NekoVpk 的变体切换**并不是**无脑解压覆盖！它严格依赖一套内部的路径标签（TaggedAssets）规则。如果你不知道这个规则，把不符合要求的文件塞进去，NekoVpk 将直接无视它们，导致你的变体切换完全失效！

## 🚫 核心规则：什么该放进 neko7z，什么必须留在外面？

根据 NekoVpk 的识别逻辑，在切换幸存者（Survivor）变体时，NekoVpk **只提取并替换以下三个路径**：
1. 玩家模型 (`models/survivors/...`)
2. 手臂模型 (`models/weapons/arms/...`)
3. UI头像 (`materials/vgui/...`)

❌ **绝对不要**把人物身体贴图（`materials/models/...`）或者声音放进 `.neko7z` 中！
由于材质体积巨大，NekoVpk 被设计为**不切换材质**。你应该把**所有变体的材质贴图**都直接放在 VPK 的外部根目录下，并在编译 `.mdl` 时通过 `$CDMaterials` 让不同变体的模型指向不同的材质文件夹。

## 📦 目录结构演示（以替换 8 人的 Mod 为例）

### 1. `.neko7z` 内部长什么样？（严格白名单）
用 7-Zip 打开你的 `.neko7z` 文件，它的根目录**只能**长这样：
```text
📦 1.neko7z
 ┣ 📂 materials
 ┃  ┗ 📂 vgui             <-- 只有 UI 头像能放这！绝对不要把人物皮肤材质放进来！
 ┗ 📂 models
    ┣ 📂 survivors        <-- 身体模型 (.mdl, .vvd, .vtx)
    ┗ 📂 weapons
       ┗ 📂 arms          <-- 手臂模型
```

### 2. VPK 的完整外部结构（“上班族”逻辑）
为了保证原版游戏的底层兼容，VPK 的根目录必须留至少 1 个幸存者的模型在外面“上班”。其余的放入默认补充包 `0.neko7z`。
**注意：** 后续的变体包（`1.neko7z`, `2.neko7z`）必须是**完整的**（即包含 8 个人全部的模型和 UI），因为它们切换时需要覆盖掉外面正在上班的模型！

**完整打包结构示例：**
```text
C:\YourModFolder
│  addoninfo.txt
│
├─materials
│  ├─models       <-- ⚠️【重要】所有变体、所有角色的皮肤贴图，全放外面！
│  └─vgui         <-- 外面“上班”的那个幸存者的 UI 头像
│
├─models          <-- 外面只留至少 1 个幸存者“上班”（比如 Gambler 尼克）
│  ├─survivors
│  │      survivor_gambler.mdl
│  └─weapons
│      └─arms
│              v_arms_gambler_new.mdl
│
└─nekovpk         <-- 存放 neko7z 的专用目录
        0.neko7z  <-- 默认补充包：装有【剩下 7 个人】的模型和 UI
        1.neko7z  <-- 变体包1：装有【完整 8 个人】的模型和 UI
        2.neko7z  <-- 变体包2：装有【完整 8 个人】的模型和 UI
```

## 🛠️ （可选）配置多变体下拉菜单 (addoninfo.txt)
如果提供了多个变体，你可以选择在根目录的 `addoninfo.txt` 中写入配置。
* 只有默认的0.neko7z就不用写了 程序不会去读的
* 不写 `nekovpk_active7z` 时，系统默认值为 `0`。
* 不写 `nekovpk_7zname` 时，UI 的下拉菜单不显示(别名)

```vdf
"AddonInfo"
{
    "addonTitle"        "My Awesome Character Mod"
    "addonAuthor"       "YourName"
    // addonversion...
    
    "nekovpk_active7z"  "0" // 当前活动包 '0'就是少一个幸存者的包是0.neko7z
    "nekovpk_7zname"    "1=战损版|2=白丝版|3=黑丝版" // UI显示的(别名)（多个别名必须用 | 隔开）
}
```

---
<br>

<h1 id="english">How to Create a Neko7z Mod</h1>

Neko7z is a special embedded format invented by Starfelll for NekoVpk, allowing you to pack multiple variants into a single VPK file.
**⚠️ CRITICAL MECHANIC WARNING:** NekoVpk's variant switching is **NOT** a simple "extract and overwrite everything" process! It strictly relies on an internal path whitelist (`TaggedAssets`). If you pack unrecognized files into the `.neko7z`, NekoVpk will completely ignore them, and your variant switch will fail!

## 🚫 Core Rule: What goes in neko7z, and what MUST stay outside?

According to NekoVpk's source code, when switching a Survivor variant, NekoVpk **ONLY extracts and replaces the following three paths**:
1. Player models (`models/survivors/...`)
2. Arm models (`models/weapons/arms/...`)
3. UI Avatars (`materials/vgui/...`)

❌ **NEVER** put character skin textures (`materials/models/...`) or sounds into the `.neko7z`!
Because textures are heavy, NekoVpk is designed **not to switch textures**. You should place **all texture files for ALL variants** directly in the external VPK root. When compiling your `.mdl`, use `$CDMaterials` to point the different variant models to their respective texture folders.

## 📦 Directory Structure Guide (Example: 8-Survivor Replacement)

### 1. Internal Structure of `.neko7z` (Strict Whitelist)
If you open your `.neko7z` with 7-Zip, the root **must** look exactly like this:
```text
📦 1.neko7z
 ┣ 📂 materials
 ┃  ┗ 📂 vgui             <-- ONLY UI avatars go here! NEVER put character skins here!
 ┗ 📂 models
    ┣ 📂 survivors        <-- Body models (.mdl, .vvd, .vtx)
    ┗ 📂 weapons
       ┗ 📂 arms          <-- Arm models
```

### 2. External VPK Structure (The "On-Duty" Logic)
To maintain engine compatibility, you must leave at least 1 survivor model "on-duty" outside in the VPK root. The remaining 7 go into the default supplementary pack `0.neko7z`.
**Note:** Subsequent variant packs (`1.neko7z`, `2.neko7z`) MUST be **complete** (containing models and UI for ALL 8 survivors) because they need to overwrite the "on-duty" model when switched!

**Complete Structure Example before packing:**
```text
C:\YourModFolder
│  addoninfo.txt
│
├─materials
│  ├─models       <-- ⚠️【CRITICAL】Skin textures for ALL variants and ALL characters go here!
│  └─vgui         <-- UI avatar for the "on-duty" survivor
│
├─models          <-- Leave at least 1 survivor "on-duty" outside (e.g., Gambler)
│  ├─survivors
│  │      survivor_gambler.mdl
│  └─weapons
│      └─arms
│              v_arms_gambler_new.mdl
│
└─nekovpk         <-- Dedicated directory for neko7z
        0.neko7z  <-- Supplementary Pack: Models/UI for the 【Remaining 7】
        1.neko7z  <-- Variant Pack 1: Models/UI for 【ALL 8】
        2.neko7z  <-- Variant Pack 2: Models/UI for 【ALL 8】
```

## 🛠️ (Optional) Configuring the Dropdown Menu (addoninfo.txt)
If you provide multiple variants, you can optionally add configuration to your `addoninfo.txt` in the root directory.
* If you only have the default `0.neko7z`, you don't need to write this section at all; the program won't read it.
* If `nekovpk_active7z` is omitted, the system defaults to `0`.
* If `nekovpk_7zname` is omitted, the UI dropdown menu will not display aliases.

```vdf
"AddonInfo"
{
    "addonTitle"        "My Awesome Character Mod"
    "addonAuthor"       "YourName"
    // addonversion...
    
    "nekovpk_active7z"  "0" // Current active pack. '0' means the pack missing one survivor is 0.neko7z
    "nekovpk_7zname"    "1=Damaged|2=White|3=Black" // Aliases displayed in the UI (Multiple aliases must be separated by |)
}
```

---
<br>

<h1 id="日本語">Neko7z形式のMod作成方法</h1>

Neko7zは、StarfelllによってNekoVpkのために考案された特殊な組み込みフォーマットで、1つのVPKファイルに複数のバリアント（バージョン）を同梱できます。
**⚠️ 重要な仕様警告：** NekoVpkのバリアント切り替えは、単純に「解凍して全てを上書きする」ものではありません！内部のパスホワイトリスト（`TaggedAssets`）に厳密に依存しています。許可されていないファイルを `.neko7z` に入れても、NekoVpkはそれらを完全に無視し、切り替えは機能しません！

## 🚫 コアルール：neko7zに入れるべきもの、外部に残すべきもの

NekoVpkのソースコードの仕様により、生存者（Survivor）バリアントを切り替える際、NekoVpkは**以下の3つのパスのみを抽出・置換します**：
1. プレイヤーモデル (`models/survivors/...`)
2. 腕モデル (`models/weapons/arms/...`)
3. UIアイコン (`materials/vgui/...`)

❌ **絶対に** キャラクタースキン（`materials/models/...`）や音声を `.neko7z` に入れないでください！
テクスチャはファイルサイズが大きいため、NekoVpkは**テクスチャを切り替えない**ように設計されています。**全バリアントの全てのテクスチャ**は、VPKのルートディレクトリに直接配置してください。`.mdl` をコンパイルする際に `$CDMaterials` を使用し、各バリアントのモデルがそれぞれのテクスチャフォルダを参照するように設定します。

## 📦 ディレクトリ構造の解説（8人置き換えModの例）

### 1. `.neko7z` の内部構造（厳密なホワイトリスト）
7-Zip等で `.neko7z` を開いた際、ルートディレクトリは**必ず**以下の構造になっていなければなりません：
```text
📦 1.neko7z
 ┣ 📂 materials
 ┃  ┗ 📂 vgui             <-- UIアイコンのみ！スキン素材は絶対に入れないでください！
 ┗ 📂 models
    ┣ 📂 survivors        <-- ボディモデル (.mdl, .vvd, .vtx)
    ┗ 📂 weapons
       ┗ 📂 arms          <-- 腕モデル
```

### 2. VPKの外部構造（「出勤」ロジック）
ゲームエンジンとの互換性を保つため、VPKのルートディレクトリには、少なくとも1人の生存者モデルを「出勤（デフォルト状態）」として配置しておく必要があります。残りの7人はデフォルト補完パック `0.neko7z` に入れます。
**注意：** 追加のバリアントパック（`1.neko7z`、`2.neko7z`）は**完全な状態**（8人全員のモデルとUIを含む）である必要があります。切り替える際に、外部で「出勤」しているモデルを上書きする必要があるためです！

**パッキング前の完全な構造例：**
```text
C:\YourModFolder
│  addoninfo.txt
│
├─materials
│  ├─models       <-- ⚠️【重要】全バリアント・全キャラクターのスキン素材は全てここに配置！
│  └─vgui         <-- 外部で「出勤」している生存者のUIアイコンのみ
│
├─models          <-- 少なくとも1人の生存者を外部に配置（例：Gambler）
│  ├─survivors
│  │      survivor_gambler.mdl
│  └─weapons
│      └─arms
│              v_arms_gambler_new.mdl
│
└─nekovpk         <-- neko7z専用ディレクトリ
        0.neko7z  <-- 補完パック：【残り7人】のモデルとUI
        1.neko7z  <-- バリアント1：【8人全員】のモデルとUI
        2.neko7z  <-- バリアント2：【8人全員】のモデルとUI
```

## 🛠️ （オプション）ドロップダウンメニューの設定 (addoninfo.txt)
複数のバリアントを提供する場合、任意でルートディレクトリの `addoninfo.txt` に設定を追加できます。
* デフォルトの `0.neko7z` が1つだけの場合は、記述する必要はありません（プログラムは読み込みません）。
* `nekovpk_active7z` を省略した場合、システムのデフォルト値は `0` になります。
* `nekovpk_7zname` を省略した場合、UI のドロップダウンメニューにはエイリアス（別名）が表示されません。

```vdf
"AddonInfo"
{
    "addonTitle"        "My Awesome Character Mod"
    "addonAuthor"       "YourName"
    // addonversion...
    
    "nekovpk_active7z"  "0" // 現在アクティブなパック。'0'は1人生存者が欠けている補完パックが 0.neko7z であることを意味します
    "nekovpk_7zname"    "1=ダメージ版|2=白バージョン|3=黒バージョン" // UIに表示されるエイリアス（別名）（複数を指定する場合は必ず | で区切る）
}
```