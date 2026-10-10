# DigiPhant 實際設定報告

Fresh checkout 補充：本機使用 Windows PowerShell 的 `powershell -NoProfile -ExecutionPolicy Bypass`，不需要 `pwsh`。SavannahCourse 已在學生 repo 追蹤，fresh checkout 不要無條件複製或覆蓋它；restore script 只還原缺少的 Elephant 與 metadata。若外部來源缺少 Savannah，才從保存來源補回並保留全部 `.meta`。

## 專案事實

- Windows 11 Pro 10.0.26200 x64；AMD Ryzen 7 PRO 8840U。
- Unity `6000.6.3f1`（revision `45d8eee7de74`）、URP `17.6.0`、Linear colour space。
- 專案根目錄：`C:\Users\violi\Projects\Katachi_Digiphant`。
- `DigiPhantStarter/` 位於專案根目錄、`Assets` 之外。來源是 `https://github.com/kommanderpi/studentstarter`，固定來源 commit `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f`。
- Python `3.12.10`、MediaPipe `0.10.35`、OpenCV contrib `4.14.0.94`；環境預期在 `DigiPhantStarter/Tracking/.venv/`。
- Savannah 獨立來源 checkout：`C:\Users\violi\Projects\studentstarter-savannah-main`，upstream main 為上述 commit。

## 資料路徑

```text
webcam → Tracking/bridge.py + MediaPipe → localhost UDP
  5055 tracking JSON / 5056 group commands / 5057 JPEG preview
    → DigiPhantController (assignment, calibration, mapping, smoothing)
      → DigiPhantLocomotion (travel, turn, gait, camera follow)
        → Elephant prefab + SavannahCourse root
```

影像與 tracking 全部在本機處理。匯入資產是 `Assets/Elephant/`、`Assets/DigiPhant/`、`Assets/SavannahCourse/`，均保留 `.meta`；Elephant 與 Savannah 已完成來源 byte-identical provenance 比對。Vendor elephant controller 在 instance 上停用，避免與自訂 Animator/joint control 衝突。

## 場景與 Savannah

開啟 `Assets/Scenes/SampleScene.unity`。原始模板是 `Assets/StudentWork/OriginalTemplateSampleScene.unity`；注入前備份為 `Assets/Scenes/SampleScene_BeforeSavannah.unity`。使用 **DigiPhant > Savannah > Inject Course into Open Scene**，只加入/替換生成的 course root，保留 elephant、camera、tracking 與 mappings。課程使用 layout 4、固定 scale、seed `20261005`；travel radius 設為 `30`。

已確認的 Unity markers 是 `SAVANNAH_VALIDATION_OK`、`SAVANNAH_INJECTION_OK`、`DIGIPHANT_SAVANNAH_VALIDATED`。它們只證明 package/generator/injection 檢查，不代表真人控制已完成。

## Solo seated / upper-body 操作

目前設定為一人、`Seated / upper body`、`Camera`、travel radius 30。停止 Play 後重新 Play，選上述模式，等 performer label 出現，再按 `Set neutral pose (10 seconds)` 並保持頭、肩、雙手可見。改 group 或 mode 後按 `Reassign people`，再重新 calibration。

一人 preset 把九個 body controls 指定給 P1。來源三人 preset 的實際 body mapping 是：前腿 P1 的 LeftFootLift/RightFootLift；後腿 P2 的 LeftFootLift/RightFootLift；Head turn、Trunk curl、Left ear、Right ear 為 P3；Tail sway 為 P2。Camera locomotion 的 Forward 以 P1 左手高度控制前進/後退，Steering 使用 Lean。Seated bridge 會以 hand-height 取代 foot-lift movement。追蹤遺失時 movement 停止並平滑回中；恢復 landmarks 後才繼續。

## Camera 修正與測試

### 三區相機預覽（目前 overlay）

`Tracking/bridge.py` 會在送出 JPEG preview 前，把畫面視覺上分成三個等寬區域，並以綠色、2 px 垂直線標示 normalized X = `1/3` 與 `2/3`：左側為 `HAND ZONE`，中間為 `POSE 1`，右側為 `POSE 2`。目前左側另有 MediaPipe Tasks GestureRecognizer：它在骨架與 overlay 繪製前，對左側可見區域做乾淨 crop；使用 `--mirror-preview` 時，這個可見左區對應 raw frame 的右側三分之一。手勢辨識已加入，但 pose ROI 仍未限制：現有 MediaPipe pose detection 仍處理完整 frame，因此中、右區的既有 pose tracking 維持不變，左區也仍可能被 pose detection 使用。`POSE 1`、`POSE 2` 是畫面標籤，不代表 performer ID 或新的 body assignment。

Unity 的 `DigiPhantCameraPreview` 會先依現有設定 mirror preview，再顯示這個 overlay；因此鏡像後的文字仍保持可讀。preview 尺寸可在 Inspector 或 Game view/controller 的 `Camera Preview Width` 手動調整，現有範圍為 `180..700`，這項設定仍保留。修改 bridge 後需先 Stop Play，再重新 Play，讓 Unity 啟動最新的 bridge 程式。

這次三區 overlay 的歷史離線驗證包含 14 個 project tracking tests；它涵蓋 label、mirror/input immutability、datagram byte cap、AR/UDP/features 與 recorder。以 synthetic 320×240 frame 產生的 `Logs/zone-preview-check.png` 為 4487 bytes，三條標籤與邊界可讀；這不是 webcam 擷取。後續手勢更新的獨立驗證為 20 個 Python tests（另加 pycompile、bootstrap matching locked-file passes，以及 strict `git apply --check --whitespace=error`），目前最新測試圖的 synthetic `Closed_Fist` preview 為 320×240、6095 bytes，兩條分隔線與手勢名稱/信心度可在 HAND ZONE 內顯示。Unity C# 的離線編譯與 initial import 是先前歷史檢查結果；修改後仍需讓 Unity 重新編譯，請 Stop Play、等待 script compilation 後再 Play。bridge 的 live 啟動應使用實際 Unity launcher（`--no-window`），不是把離線檢查當作 webcam/Unity live test 的替代品。

原始快速關閉原因是 Windows UDP `recvfrom()` 收到 `ConnectionResetError`（10054/ICMP port unreachable）後 bridge 退出。`TrackingPatches/bridge-windows-udp-reset.patch` 現在同時包含 UDP reset recovery 與 Windows DirectShow camera backend；`receive_datagrams()` 略過 reset，controller `nextReceiverRetry` throttle 避免反覆 reload。camera default index 是 `0`，第二台才用 `--camera 1`。

獨立 bridge 測試已通過：最新 DirectShow patch commit `f8c195d`，隔離 ports `5065–5067`，總計 29.8 秒（啟動 4.8 秒、live preview 25.0 秒），收到 25 張 JPEG 與 17 個 valid version-1/count-1/upper-body JSON packet。前 8 秒沒有 tracking receiver 時 bridge 仍持續，接上 receiver 後恢復；測試程序已乾淨停止，未產生影像或影片。`DigiPhant-TrackingPatchCheck2` 的 12 tests 通過；target 與 isolated `Assembly-CSharp` DLL 都含 `nextReceiverRetry`，scripts 編譯 log 無 CS error。`DigiPhant-ProjectReconstructionCheck` 的 Unity initial import/script compile 也已以 batchmode return code 0 完成。

若 Game view 的 Retry 還使用舊 runtime 設定，先 Stop Play、再 Play，然後按 Retry；serialized Camera mode 不一定會更新既有 runtime instance。Unity initial import/script compile 已完成，但 Unity Game view 持續 preview 尚未宣稱通過。真人 assignment、calibration、gesture/elephant response 也尚未完成；bridge 測試中的 `people: []` 只表示沒有偵測到人，不能當成人體控制通過。

## 錄影

完成 Camera calibration 後保持 Game view 可見 preview 與 elephant，按 `Record performance`，停止後檢查：

```text
Recordings/<session>/performance.mp4
```

輸出是約 1280×720、最高 10 fps 的 Game view，沒有 microphone audio。需實際播放確認，再手動上傳團隊 Drive；目前沒有 Drive URL。真人 Game view recording、MP4 playback、Drive upload 尚未驗證，影片不提交 Git。

## 變更與 Git

主要變更檔案：`Assets/Scenes/SampleScene.unity`、`Assets/Scenes/SampleScene_BeforeSavannah.unity`、`Assets/StudentWork/OriginalTemplateSampleScene.unity`；`Assets/StudentWork/Editor/DigiPhantSavannahSetup.cs`（dirty-scene guard、一次性 setup/validation requests、solo profile）；`Assets/DigiPhant/Runtime/DigiPhantController.cs`（receiver retry throttle）；`TrackingPatches/bridge-windows-udp-reset.patch`（UDP reset recovery + DirectShow backend）；`Tools/RestoreDigiPhantStarter.ps1`（只還原缺少的 Elephant shared asset 並套用 patch）。

`DigiPhant-ProjectReconstructionCheckFinal` 已通過 guarded restore、Elephant 完整 hash 一致與 patch reverse check；restore commit `ca81d8c`。學生 Git 已準備為輕量版本（約 1.37 MB），排除影片、Elephant/starter heavy clone、`.venv` 與 Unity caches；目前沒有 remote/push URL。相關工作樹 commits 為 `f8c195d` 與 `ca81d8c`。

## Fresh checkout 復原

以下命令在學生 repo 專案根目錄執行；先確認 `python --version` 是 3.12.10。這台機器的 `py --list` 沒有註冊 Python，因此不要依賴 `py -3.12`；若 `python` 不是 3.12，改用已確認的 `C:\Users\violi\AppData\Local\Programs\Python\Python312\python.exe`。

```powershell
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
Push-Location DigiPhantStarter
git checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
Pop-Location
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\RestoreDigiPhantStarter.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\InstallGestureRecognizerModel.ps1
python -m venv .\DigiPhantStarter\Tracking\.venv
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m pip install -r .\DigiPhantStarter\Tracking\requirements.txt
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m pip check
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m unittest discover -s .\DigiPhantStarter\Tracking -v
```

restore script 只在 `Assets/Elephant` 與其 metadata 不存在時還原它們，並套用 patch；不要重複套用已存在的 patch。SavannahCourse 與全部 `.meta` 已納入學生 repository，fresh checkout 不需要再次複製；不要覆蓋已追蹤的 `Assets/DigiPhant`、`Assets/StudentWork` 或 Savannah adaptations。`Library/`、`Temp/`、`Logs/`、`.venv/` 與錄影不納入 Git；Python 環境需依上述命令重建，錄影需另行保存到 Drive。

Windows UDP 行為參考 [Microsoft Winsock recvfrom](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-recvfrom)，camera backend/index 參考 [OpenCV Video I/O flags](https://docs.opencv.org/4.x/d4/d15/group__videoio__flags__base.html)。
## GestureRecognizer（左側 HAND ZONE）

目前 bridge 對 preview 的可見左三分之一使用 MediaPipe Tasks `GestureRecognizer`。在 `--mirror-preview` 下，可見左區對應 raw camera 的右三分之一；每個 frame 都先以 monotonic millisecond timestamp 在 VIDEO mode 做乾淨 crop，再進行手勢辨識，預設 hand confidence threshold 為 `0.5`。完整 frame 的 pose tracking 及既有 UDP JSON/JPEG 訊息維持不變；gesture 不會新增 pose JSON 欄位或事件，也不會直接控制 elephant，但 gesture 文字會烘焙在既有 JPEG preview 像素中，因此可在 Unity 預覽看到。

顯示的支援集合為 `Closed_Fist`、`Thumb_Up`、`Thumb_Down`、`Victory`、`Open_Palm`、`None`。MediaPipe 真正回傳的 `None` 會保留其 confidence；沒有偵測到手時立即顯示 `Gesture: None` 且不顯示分數。default model 可能回傳 `Pointing_Up` 或 `ILoveYou`，這些 unsupported top-1 會映射成 `None`，不會改用較低排名的 label。模型遺失、損壞、載入失敗或 inference 失敗時顯示 `Gesture Unavailable`，camera preview 與 pose tracking 繼續；可用 `--disable-gestures` 關閉。

官方 model 放在 `Assets` 之外的 `DigiPhantStarter/Tracking/gesture_recognizer.task`（8,373,440 bytes，SHA-256 `97952348cf6a6a4915c2ea1496b4b37ebabc50cbbf80571435643c455f2b0482`）。`Tools/GestureRecognizerModel.json` 記錄固定來源與 hash。fresh checkout 可用以下命令還原：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\InstallGestureRecognizerModel.ps1
```

installer 會驗證既有 matching file、保留被鎖定的檔案，並使用由 installer 擁有的 temporary file 下載；mismatch 時拒絕覆寫，不刪除使用者檔案。除了既有 MediaPipe `0.10.35` 環境外不需要新 Python dependency。來源說明：[MediaPipe Gesture Recognizer Python guide](https://ai.google.dev/edge/mediapipe/solutions/vision/gesture_recognizer/python) 與 [Google official example](https://github.com/google-ai-edge/mediapipe-samples/blob/main/examples/gesture_recognizer/python/gesture_recognizer.ipynb)。

若要用官方 sample fixture 做真實 model integration test，可明確下載至 `DigiPhantStarter/Tracking/.test-fixtures/`（不把照片或 model 加入 Git）：

```powershell
New-Item -ItemType Directory -Force .\DigiPhantStarter\Tracking\.test-fixtures
Invoke-WebRequest https://storage.googleapis.com/mediapipe-tasks/gesture_recognizer/thumbs_up.jpg -OutFile .\DigiPhantStarter\Tracking\.test-fixtures\thumbs_up.jpg
```

目前 reviewer 的 20 tests 已包含 ROI、unsupported top-1、no-hand、真實 `None`、Unavailable rendering、Unity JSON 不增欄位與 JPEG cap；官方 `thumbs_up.jpg` 實測為 `Thumb_Up` 且 score > `0.5`，接續 blank frame 為 `None` 且無 score。沒有下載 fixture 的 fresh checkout 會跳過需要該 sample 的 integration test；本機這次已有實際 model check，並非把缺 fixture 當成通過。這些都是 offline/model checks，不是 live camera 或 Unity session。

要做真人測試，先 Stop Play 再 Play，讓 Unity 啟動目前 bridge；將一隻完整的手放在可見 `HAND ZONE`，依序試 fist、thumbs up/down、Victory、open palm，移出區域後應顯示 `Gesture: None`。gesture label 目前不影響 elephant；pose tracking 仍可獨立移動 elephant。六種手勢的 live performance、recovery 與 FPS 仍需在裝置上測試。
