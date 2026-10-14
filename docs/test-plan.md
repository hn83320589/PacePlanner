# PacePlanner 測試計畫

## 1. 目的與範圍
確認 PacePlanner 是否符合需求書的全部項目，包括功能需求、技術限制、14 條驗收標準和交付物。所有測試都可以重複執行，執行結果記錄在 [`test-report.md`](test-report.md)。

## 2. 測試層級與工具

| 層級 | 涵蓋內容 | 工具 | 執行方式 |
|---|---|---|---|
| L1 單元測試 | Core（GPX、統計、策略、補給、格式、匯出、範例檔）、Data（SQLite） | xUnit | `dotnet test PacePlanner.sln -c Release` |
| L2 UI 端對端 | 實際啟動 App，以使用者操作方式驗證畫面行為 | Windows UI Automation，加上 Win32 訊息操作檔案對話框 | `powershell -File tests/e2e/Run-E2E.ps1` |
| L3 匯出驗證 | CSV 用 Excel 開啟、HTML 列印成 PDF 計算頁數 | Excel COM、Edge headless `--print-to-pdf` | 包含在 L2 腳本中 |
| L4 靜態檢查 | 專案目標框架、相依套件、授權、網路呼叫、CI 設定、交付物 | `dotnet list package`、原始碼搜尋 | 依第 5 節指令執行 |
| L5 CI | Windows runner 建置與測試 | GitHub Actions | push 後查詢 run 結果 |

**環境**：Windows 11、x64、.NET SDK 9.0.x，App 以 Release 建置。L2 以**非系統管理員權限**啟動 App，模擬一般使用者。

**資料隔離**：L2 執行前會先把 `%LocalAppData%\PacePlanner\plans.db` 備份起來並清空，結束後再還原，不會影響使用者原有的方案。

## 3. 需求追溯矩陣

| 需求 | 測試案例 |
|---|---|
| 匯入 GPX（trk、多個 trkseg）並顯示 5 項統計 | U-GPX、U-COURSE、U-SAMPLE、E02、E03、E04 |
| 格式錯誤或沒有軌跡點時顯示訊息、不崩潰 | U-GPX-BAD、U-SAMPLE-BAD、E05 |
| 官方距離與自訂距離、目標時間 hh:mm:ss | U-TIME、E06、E11、E13 |
| 三種配速策略 | U-PACE、E06、E07、E08 |
| 分段表（每公里一段、最後一段依實際距離、各欄位） | U-PACE-SEG、E06 |
| 補給（每 N 公里、指定清單、停留秒數） | U-AID、E09、E10、E12 |
| 海拔剖面圖（海拔、公里分隔線、補給標記） | U-CHART、E09（截圖檢視） |
| 方案管理（新增、複製、改名、刪除、重開） | U-REPO、E14、E19 |
| 匯出 CSV（BOM）與 HTML（A4 單頁） | U-EXPORT、E17、E18 |
| Core 為純 net9.0、不引用 WinUI | S01 |
| 不使用付費套件、不呼叫網路 | S02、S03 |
| SQLite 位置在 `%LocalAppData%\PacePlanner\` | S04、E01 |
| 至少 20 個單元測試 | L1 統計 |
| README、範例資料、CI | S05、S06、L5 |

## 4. 測試案例

### 4.1 L1 單元測試（自動）
| ID | 對應測試類別 | 驗收標準 |
|---|---|---|
| U-GPX | `GpxParserTests` | #2、#3 |
| U-COURSE | `CourseTests` | #1 |
| U-SAMPLE | `SampleFilesTests` | #1、#2、#3 |
| U-PACE | `PaceCalculatorTests`（Even、NegativeSplit、GradeAdjusted、GradeFactor） | #4、#5、#6 |
| U-AID | `PaceCalculatorTests.AidStations_*`、`AidStationListParserTests` | #7 |
| U-TIME | `TimeFormatTests` | #8 |
| U-CHART | `ProfileChartLayoutTests` | #9 |
| U-EXPORT | `ExportTests` | #11 |
| U-REPO | `PlanRepositoryTests` | #10 |

**通過條件**：全部通過，總數 ≥ 20。

### 4.2 L2 UI 端對端（自動化操作）

| ID | 步驟 | 預期結果 | 驗收標準 |
|---|---|---|---|
| E01 | 資料庫清空後啟動 | 程式正常開啟，自動建立「我的第一個方案」，`%LocalAppData%\PacePlanner\plans.db` 已建立 | #10 |
| E02 | 匯入 `river-park-10k.gpx` | 總距離 10.00 km、爬升 60 m、下降 60 m、最高 55 m、最低 10 m | #1 |
| E03 | 匯入 `multi-segment-5k.gpx` | 4.80 km、爬升 42 m、下降 18 m | #1、#2 |
| E04 | 匯入 `noisy-gps-3k.gpx`，再匯入 `no-elevation-5k.gpx` | 噪聲檔 3.00 km、爬升 30 m；無海拔檔 5.00 km、海拔顯示「無資料」，並出現「沒有海拔資料」提示 | #1 |
| E05 | 依序匯入 `invalid/` 下的 5 個檔案 | 每個檔案都顯示含關鍵字的錯誤訊息，程式不崩潰，原本的賽道統計保持不變 | #3 |
| E06 | 河濱賽道、10K、0:50:00、等速 | 共 10 段，每段配速都是 5:00，最後累計 0:50:00，各段用時累加等於累計欄 | #4、#8 |
| E07 | 負分段，N = 10 | 前 5 段配速都慢於後 5 段，最後累計 0:50:00 | #5 |
| E08 | 坡度調整 | 坡度 +3% 的段 > 平路段 > 坡度 −3% 的段（配速秒數），最後累計 0:50:00 | #6 |
| E09 | 補給「每 N 公里」N = 2.5，停留 30 秒 | 第 3、5、8 段各有 30 秒停留，最後累計仍是 0:50:00，用時累加一致；截圖檢查剖面圖上的補給標記 | #7、#8、#9 |
| E10 | 補給「指定公里數」輸入「3, 7.5」 | 第 3、8 段有停留，最後累計 0:50:00 | #7 |
| E11 | 目標時間輸入 `abc` | 顯示「格式錯誤」訊息，分段表清空，程式不崩潰；改回正確值後恢復 | 需求 |
| E12 | 指定公里數輸入 `x` | 顯示補給站公里數無效的訊息 | 需求 |
| E13 | 距離改為自訂 12 km | 共 12 段，摘要顯示「已等比例縮放」 | 需求 |
| E14 | 改名為「E2E 河濱 10K」、複製為「E2E 複本」、新增「E2E 刪除用」再刪除 | 方案清單正確反映每個動作 | #10 |
| E15 | 新增「E2E 多段」，匯入多段檔、距離選 5K | 共 5 段，統計 4.80 km | #2 |
| E17 | 匯出 CSV | 檔案以 UTF-8 BOM 開頭；用 Excel 開啟時 A1 為「段」、F1 為中文標題；共 11 列 | #11 |
| E18 | 匯出列印版 HTML | 檔案存在；Edge 列印成 PDF 為 1 頁、A4 尺寸（595×842 pt） | #11 |
| E19 | 把目標改成 0:49:00、不按儲存就關閉視窗，再重新啟動 | 方案清單與關閉前相同，「E2E 刪除用」不存在；每個方案重新開啟後的統計和分段表與關閉前完全相同（含 0:49:00 的變更） | #10 |

### 4.3 L4 靜態檢查
| ID | 檢查 | 方法 | 預期 |
|---|---|---|---|
| S01 | Core、Data 為純 net9.0，不引用 WinUI | 讀取 csproj 的 TargetFramework 與參考 | `net9.0`，沒有 WindowsAppSDK |
| S02 | 套件皆為免費授權 | `dotnet list package --include-transitive` 搭配 NuGet 授權資訊 | MIT 或 Apache-2.0 等免費授權 |
| S03 | 不呼叫外部網路 | 在 `src/` 搜尋 `HttpClient`、`WebRequest`、`Socket`、`http(s)://` | 只有 XML 命名空間字串，沒有網路呼叫 |
| S04 | 資料庫路徑 | 讀取 `PlanRepository.DefaultDatabasePath`，並對照 E01 | `%LocalAppData%\PacePlanner\plans.db` |
| S05 | README 內容 | 檢視章節 | 包含建置步驟、截圖、架構、策略說明、設計取捨、已知限制、徽章 |
| S06 | 範例與 CI | 檢查檔案 | ≥ 3 個 GPX、`expected-stats.md`、`ci.yml` 使用 windows runner 與 `setup-dotnet` 9.0.x |

## 5. 執行指令
```powershell
dotnet build PacePlanner.sln -c Release
dotnet test  PacePlanner.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/e2e/Run-E2E.ps1   # 結果輸出到 tests/e2e/out/
```

## 6. 通過條件
* L1、L2、L4、L5 全部通過。
* 需要人眼判斷的項目（剖面圖、列印版面）附上截圖，並在報告中記錄檢視結論。
* 有任何失敗時，修正後重新執行完整的 L1 與 L2。
