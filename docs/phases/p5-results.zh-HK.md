# P5：儲存遷移計劃

2026-10-20。交付：[分階段計劃](../storage-migration-plan.zh-HK.md)。

已按目前 source 盤點 22 個 Mongo collection，逐個列 PostgreSQL 去向、key/index、CAS／idempotency，同三階段檔案範圍、驗收及 rollback。包含 R2 object identity／upload outbox、短效播放 URL、shared Data Protection keys、activity 容量計算、本機 Postgres 建議、清 DB 批准模板，同 cloud/account 下一步。

官方來源已於文件逐項連結，查閱日期 2026-10-20。無新增 migration、package、雲端資源，無讀取／清除原 DB；source inventory 唔等同 production schema/data export。

完成條件：文件盤點同計劃完成；每階段 live database／R2／Cloud Run checks 留待實作，未宣稱已部署。套件 patch、region 報價、帳戶登入方式及 legacy identity 遷移要於下一階段定案。

範圍外改動：無。
