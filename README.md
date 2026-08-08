# Bakery Light Converter

Unity Light から Bakery Light へ一括変換するUnityエディタツールです。

## 機能

- Unity の標準ライト（Point、Spot、Directional、Area）を Bakery 用ライトコンポーネントに一括変換
- 色、強度、範囲、スポット角度、クッキーテクスチャなどの主要パラメータを自動コピー
- Undo/Redo対応で安全に変換
- 選択中のライトのみ変換、またはシーン内の全ライトを一括変換

## 使用方法

### 1. 選択中のライトを変換
1. Hierarchy または Scene View でライトオブジェクトを選択
2. メニューから `Tools` → `BakeryLightConverter` → `Convert Selected Lights` を選択
3. または `Alt + Shift + B` のショートカットキーを使用

### 2. シーン内の全ライトを変換
1. メニューから `Tools` → `BakeryLightConverter` → `Convert All Lights` を選択

## 変換対応

### Point Light → BakeryPointLight
- 色（色温度対応）
- 強度
- 範囲
- クッキーテクスチャ（Cubemap として設定）

### Spot Light → BakeryPointLight
- Point Light の全機能
- スポット角度
- 内側角度（ソフトエッジ）
- クッキーテクスチャ（Cookie として設定）
- Cookieが未設定の場合、Bakery標準のスポットテクスチャを自動適用

### Directional Light → BakeryDirectLight
- 色（色温度対応）
- 強度
- HDRP の sunAngularDiameter（存在する場合）

### Area Light → BakeryLightMesh
- 色（色温度対応）
- 強度
- エリアサイズ（幅・高さ）を Transform スケールで再現

## 注意事項

- 既存の Bakery ライトコンポーネントがある場合は上書きされません
- 変換後は Undo で元に戻すことができます
- 元の Unity Light は無効化されません（必要に応じて手動で調整してください）

## 必要な環境

- Unity 2022.3.22f1
- Bakery GPU Lightmapper

## インストール

`BakeryLightConverter.cs` をプロジェクトの `Editor` フォルダに配置してください。
