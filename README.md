# Katachi DigiPhant

這是一個 Unity 6 URP 專案：以 MediaPipe webcam tracking 控制大象，並在同一個場景加入 Savannah 課程。主要場景是 `Assets/Scenes/SampleScene.unity`。

完整的實際環境、資料路徑、修改、測試證據、復原方式與待辦事項請看 [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md)。

快速開始：用 Unity `6000.6.3f1` 開啟專案並開啟 `Assets/Scenes/SampleScene.unity`，按 Play，選 `1 person`、`Seated / upper body`、`Camera`。等待自己出現在 preview 後按 `Set neutral pose (10 seconds)`，保持坐姿和雙手在畫面內。錄影輸出到 `Recordings/<session>/performance.mp4`；請自行檢查後上傳團隊 Drive，影片不會自動上傳，也不提交到 Git。

Starter clone 位於專案外的 `DigiPhantStarter/`，追蹤環境位於 `DigiPhantStarter/Tracking/.venv/`。重建流程與實際限制見 setup 報告。
