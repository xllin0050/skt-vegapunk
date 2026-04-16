## 0. 系統背景

| 項目 | 說明 |
|------|------|
| **系統性質** | 校務系統（大學/學校行政管理系統） |
| **本模組** | 公文簽核子系統（電子簽核、流程審批） |
| **資料庫** | **Sybase ASE 15.7**（Adaptive Server Enterprise） |
| **應用伺服器** | Sybase EAServer（Jaguar CTS）— CORBA 元件容器 |
| **前端** | JSP + jQuery + jQuery UI（佈署於 EAServer） |
| **後端** | PowerBuilder NVO 元件（透過 CORBA 暴露為服務） |
| **字元集** | Big5 / UTF-16 LE（校務系統使用繁體中文） |
| **PB Library** | 5 個 PBL：dw_sign, sign, sky_webbase, tpec_s61, webap |
| **EAServer 位址** | `iiop://localhost:9000`（內部部署，jagadmin 帳號） |

> **校務系統的特殊考量**：
> - 涉及學生、教職員個資 → 適用《個資法》與教育部資安規範
> - 通常需符合「資通安全責任等級」分級（中級以上需通過資安稽核）
> - 公文簽核涉及法律效力 → 流程正確性比一般 CRUD 系統更關鍵
> - 與其他校務子系統（人事 s10、組織 s90、公文 s99）共用資料庫

---

