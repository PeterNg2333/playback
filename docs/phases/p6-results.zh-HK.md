# P6：講者分離同文件抽取研究

2026-10-20。交付：[diarization](../research/diarization.zh-HK.md)、[document extraction](../research/document-extraction.zh-HK.md)。

文件抽取建議 PdfPig 0.1.16 + Open XML SDK 3.5.1，另比較 Tika；分清 PDF page、PPTX slide 同 DOCX paragraph，列明 OCR／表格／公式限制、隔離同資源上限、P8 fixture/browser 驗收。

講者分離比較 Azure Speech、pyannote community-1、sherpa-onnx 同目前 OpenRouter adapter；建議先用 Azure .NET 即時小 prototype，後備用本機 pyannote 事後補 speaker spans。包含 session/source/window/model 冪等身份、命名／Unknown 同 P13 live 計劃。

證據係查閱官方 docs、repo、model card 同 NuGet，日期同版本線逐項記錄。未安裝候選、未跑模型／parser、未上傳 diarization 音訊；廣東話真實多人準確度、Azure 實際 region quote 及 .NET/native 相容性仍要 prototype 驗證。

完成條件：研究比較、方案同 implementation 結論已交付。未知／未驗證欄位明示，唔以推測填成支援承諾。範圍外改動：無。
