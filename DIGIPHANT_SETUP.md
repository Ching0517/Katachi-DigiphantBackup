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
python -m venv .\DigiPhantStarter\Tracking\.venv
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m pip install -r .\DigiPhantStarter\Tracking\requirements.txt
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m pip check
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m unittest discover -s .\DigiPhantStarter\Tracking -v
```

若 PowerShell 找不到 `pwsh`，上面的 Windows PowerShell 命令仍可用。restore script 只在 `Assets/Elephant` 不存在時複製 Elephant/metadata，並套用 patch；不要重複套用已存在的 patch。SavannahCourse 與 `.meta` 必須從保存來源複製並保留所有 metadata；不要覆蓋 repo 已追蹤的 `Assets/DigiPhant`、`Assets/StudentWork` 或 Savannah adaptations。`Library/`、`Temp/`、`Logs/`、`.venv/`、錄影由工具重建。

Windows UDP 行為參考 [Microsoft Winsock recvfrom](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-recvfrom)，camera backend/index 參考 [OpenCV Video I/O flags](https://docs.opencv.org/4.x/d4/d15/group__videoio__flags__base.html)。
