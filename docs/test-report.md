# PacePlanner 測試報告

* **執行日期**：2026-10-14
* **受測版本**：`713f019`（`main`），Release 組態
* **環境**：Windows 11 Pro 10.0.26200、x64、.NET SDK 9.0.315、Excel、Microsoft Edge（headless）
* **依據**：[`test-plan.md`](test-plan.md)

## 總結

| 層級 | 結果 |
|---|---|
| L1 單元測試 | **105 / 105 通過**（Core 96、Data 9） |
| L2/L3 UI 端對端與匯出 | **62 / 62 項檢查通過**，0 失敗、0 略過 |
| L4 靜態檢查 | **S01–S06 全部通過** |
| L5 CI | GitHub Actions [run 38532626878](https://github.com/hn83320589/PacePlanner/actions/runs/38532626878)：**success** |

**結論：14 條驗收標準全部符合。** 第 6 節列出 3 項觀察，皆不影響驗收。

## 1. 驗收標準結果

| # | 標準 | 結果 | 證據 |
|---|---|---|---|
| 1 | 範例賽道的總距離、爬升、下降正確 | ✅ | `SampleFilesTests`；E02：10.00 km / 60 m / 60 m；E04：3.00 km / 30 m / 0 m；E03：4.80 km / 42 m / 18 m |
| 2 | 多個 trkseg 完整匯入、順序正確 | ✅ | `GpxParserTests`、`SampleFilesTests.MultiSegment5k_*`（點數 21/13/17、緯度遞增）；E03、E15 |
| 3 | 損毀、空檔、非 XML 時顯示訊息且不崩潰 | ✅ | E05：5 個錯誤檔都顯示明確訊息、程式存活、原本的賽道保留；[截圖](test-evidence/E05-invalid-gpx-error.png) |
| 4 | 等速：每段配速相同、最後一段按比例、總時間到秒 | ✅ | `PaceCalculatorTests.Even_*`；E06：10 段全為 5:00，累計 0:50:00 |
| 5 | 負分段：後半較快、總時間到秒 | ✅ | `NegativeSplit_*`；E07：前半 5:05、後半 4:55，累計 0:50:00 |
| 6 | 坡度調整：上坡慢、下坡快、總時間到秒 | ✅ | `GradeAdjusted_*`；E08：+3% 為 5:27、平路 4:57、−3% 為 4:41，累計 0:50:00 |
| 7 | 補給停留計入、抵達時間仍等於目標 | ✅ | `AidStations_*`；E09（每 2.5 km 停 30 秒，第 3、5、8 段）、E10（指定 3、7.5 km），累計都是 0:50:00 |
| 8 | 配速以分:秒顯示，用時加總與累計一致 | ✅ | `TimeFormatTests`；E06–E10 每一列都檢查「用時累加 = 累計欄」，無不一致 |
| 9 | 剖面圖顯示海拔、公里線、補給標記 | ✅ | `ProfileChartLayoutTests`；[E09 截圖](test-evidence/E09-aid-every-2.5km.png)，人工檢視：海拔曲線、9 條公里線、2.5/5/7.5 km 三個補給標記都位置正確 |
| 10 | 方案儲存、複製、改名、刪除，關閉重開後不變 | ✅ | `PlanRepositoryTests`；E14（改名、複製、新增、刪除）；E19（重開後 4 個方案的統計、目標、分段表與關閉前完全相同；未按儲存的修改也已自動儲存） |
| 11 | CSV 用 Excel 不亂碼；HTML 列印為單頁 A4 | ✅ | E17：BOM `EF BB BF`，Excel 讀到 A1「段」、F1「目標配速(分:秒/km)」；E18：PDF 1 頁、MediaBox 594.96×841.92 pt（A4）；[列印預覽](test-evidence/E18-print-preview.png) |
| 12 | ≥ 20 個單元測試，`dotnet test` 全數通過 | ✅ | 105 個，全數通過 |
| 13 | README 內容 | ✅ | S05：包含建置、截圖（3 張）、架構、計算方式、設計取捨、已知限制、徽章 |
| 14 | GitHub Actions 綠燈、README 徽章 | ✅ | L5 success；徽章指向 `ci.yml` |

## 2. L1 單元測試
```
已通過! - 失敗: 0，通過: 96，總計: 96 - PacePlanner.Core.Tests.dll (net9.0)
已通過! - 失敗: 0，通過:  9，總計:  9 - PacePlanner.Data.Tests.dll (net9.0)
```

## 3. L2/L3 UI 端對端
執行方式：`powershell -NoProfile -ExecutionPolicy Bypass -File tests/e2e/Run-E2E.ps1`。App 以非系統管理員權限啟動，執行前備份並清空使用者的資料庫，結束後還原。

| ID | 檢查項目 | 結果 | 實際值（摘要） |
|---|---|---|---|
| E01 | 啟動、自動建立預設方案、DB 位置 | 3/3 PASS | `C:\Users\…\AppData\Local\PacePlanner\plans.db` |
| E02 | 一般賽道統計 | 1/1 PASS | 10.00 km / 60 m / 60 m / 55 m / 10 m |
| E05 | 5 個錯誤檔 × （錯誤訊息、存活並保留原賽道） | 10/10 PASS | 例如「無法匯入「empty.gpx」：檔案是空的，沒有任何內容。」 |
| E06 | 等速 | 5/5 PASS | 10 段、5:00、0:50:00 |
| E07 | 負分段 N=10 | 3/3 PASS | 前半 5:05、後半 4:55 |
| E08 | 坡度調整 | 3/3 PASS | 5:27 > 4:57 > 4:41 |
| E09 | 每 N 公里補給 | 3/3 PASS | 3:30,5:30,8:30 |
| E10 | 指定公里數補給 | 3/3 PASS | 第 3、8 段 |
| E11 | 目標時間格式錯誤 | 3/3 PASS | 「目標完賽時間格式錯誤，請輸入 hh:mm:ss…」 |
| E12 | 補給清單格式錯誤 | 1/1 PASS | 「補給站公里數「x」無效…」 |
| E13 | 自訂距離 12 km | 3/3 PASS | 12 段，「GPX 實測 10.00 km 已等比例縮放為 12 km」 |
| E14 | 改名、複製、新增、刪除 | 4/4 PASS | 複本的分段表與原方案相同 |
| E03/E15 | 多 trkseg 新方案、5K | 2/2 PASS | 4.80 km / 42 m / 18 m / 35 m / 5 m；5 段、0:25:00 |
| E04 | 雜訊與無海拔 | 3/3 PASS | 3.00 km / 30 m / 0 m；5.00 km / 無資料，並顯示提示 |
| E17 | CSV、Excel | 5/5 PASS | BOM、11 列、J11 = 00:50:00 |
| E18 | HTML、PDF | 3/3 PASS | 1 頁 A4 |
| E19 | 自動儲存、重開後一致 | 7/7 PASS | 4 個方案逐一比對相同；0:49:00 已保存 |

完整逐項結果會由腳本輸出到 `tests/e2e/out/results.md`（不納入版控）。

截圖證據：[E02 匯入](test-evidence/E02-river-import.png)、[E05 錯誤](test-evidence/E05-invalid-gpx-error.png)、[E09 補給與剖面圖](test-evidence/E09-aid-every-2.5km.png)、[E04 無海拔](test-evidence/E04-no-elevation.png)、[E18 列印預覽](test-evidence/E18-print-preview.png)、[E19 重開後](test-evidence/E19-after-restart.png)

## 4. L4 靜態檢查

| ID | 結果 | 說明 |
|---|---|---|
| S01 | ✅ | Core：`net9.0`，沒有任何套件或專案參考；Data：`net9.0`，只參考 Microsoft.Data.Sqlite 與 Core |
| S02 | ✅ | 直接與間接相依共 37 個套件：MIT、Apache-2.0，或 Microsoft 授權（Windows App SDK、Windows SDK BuildTools、WebView2 允許免費再散布），沒有付費授權 |
| S03 | ✅ | `src/` 中沒有 HttpClient、WebRequest、Socket；`http://` 只出現在 XAML 與 SVG 的命名空間宣告 |
| S04 | ✅ | `Environment.SpecialFolder.LocalApplicationData\PacePlanner\plans.db`，與 E01 實測一致 |
| S05 | ✅ | README 章節齊全，含 3 張截圖與 CI 徽章 |
| S06 | ✅ | 4 個 GPX ＋ `expected-stats.md` ＋ 5 個錯誤檔；`ci.yml`：`windows-latest`、`actions/setup-dotnet@v4`、`9.0.x` |

## 5. 執行過程中發現的問題

| 問題 | 原因 | 處理 |
|---|---|---|
| 第一次 E2E 執行時，E15、E04 發生例外 | 測試腳本的全域變數 `$S`（TreeScope）被測試內的 `$s = Stats` 覆蓋（PowerShell 變數名稱不分大小寫） | 屬於**測試腳本**的問題，已改名為 `$TScope`，重跑後 62/62 通過。App 本身無需修改 |

## 6. 觀察事項（不影響驗收）
1. **XML 錯誤訊息混有英文**：XML 解析錯誤會附上 .NET 原生的英文說明，例如「檔案不是有效的 XML（第 1 行）：Unexpected end of file…」。前半段的中文已足以讓使用者理解，英文部分方便除錯。如果希望全中文，可以只保留行號與位置。
2. **新方案的預設距離是全馬（4:00:00）**：在新方案匯入 5 km 的 GPX 時，會先被縮放成 42.195 km，要再手動選 5K。這和設計一致（以選定的官方距離為準），但第一次使用可能會覺得意外。
3. **分段表的無障礙名稱**：分段表每一列在 UI Automation 中的名稱是 `SplitRow { … }`（record 的 ToString），螢幕閱讀器會念出這段文字。不影響功能。
