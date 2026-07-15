# Beam Drawing — Bảng so sánh layout ADDIN vs ĐÍCH (căn cứ đo MCP)

> Mục đích: tránh fix mù. Mỗi lần sửa → đo lại bảng này (script MCP ở cuối) → so cột ADDIN với ĐÍCH.
> Hệ toạ độ: crop-local của view. Đơn vị feet.

## Lần đo 2026-07-02 08:30 — view (10) = DLL CŨ (trước fix 08:28)

| View | cropY | thepY (giảm dần) | head Y | head X | leaderEnd |
|---|---|---|---|---|---|
| ADDIN-GOI (10) | 0..**2.14** | 1.02, 0.99, 0.91, 0.46 | 1.71/1.28/0.85/0.43 | 2.20 | Attached |
| **ĐÍCH DK2-1** | 0..**2.62** | 1.35, 1.31, 1.24, 0.78 | 2.20/1.57/1.05/0.43 | 2.53 | Attached |
| ADDIN-NHIP (10) | 0..2.14 | 1.02, 0.99, 0.56, 0.46 | 1.71/1.28/0.85/0.43 | 2.20 | Attached |
| **ĐÍCH DK2-3** | 0..2.62 | 1.35, 1.31, 0.88, 0.78 | 2.20/1.63/1.05/0.43 | 2.53 | Attached |

### Phân tích căn cứ (KHÁC BIỆT gốc)
1. **cropY addin=2.14 < đích=2.62** → crop addin hẹp hơn 0.48ft. Fix 08:28 (`CrossHeightMarginFeet=0.8`) nhắm sửa cái này — CHƯA đo lại.
2. **head X addin=2.20 vs đích=2.53** — chênh vì cropMax.X khác (1.32 vs 1.64); công thức cropMaxX+0.886 GIỐNG nhau → không phải bug.
3. **head Y**: cả 2 rải đều, đều lệch xa thép. Đích head cao hơn (2.2) vì crop cao hơn.
4. **thepY cấu trúc GIỐNG** (3 chụm trên + 1 xa dưới) — đích rộng hơn chút. Dầm test khác nhau.
5. leaderEnd=Attached cả 4 view. Loại tag/số lượng GIỐNG (3 MRA + 1 đai).

### KẾT LUẬN
- Bug X (head lệch cột) đã fix xong (mọi head cùng X trong từng view).
- Leader "xiên" ở addin (10) do crop hẹp + head rải đều xa thép. Fix 08:28 = nới crop + head bám thép (SpreadClampedToCrop).
- **CẦN: đo lại view mới từ DLL 08:28** để xác nhận crop đã cao + head bám thép. Nếu head Y mới ≈ thepY → leader gọn.

## Lần đo 2026-07-02 08:35 — view (11) = DLL 08:28 (SAU fix crop + head bám thép)
| View | cropY | thepY | head Y |
|---|---|---|---|
| ADDIN-GOI (11) | 0..**3.08** | 1.49,1.46,1.38,0.93 | 1.93/1.43/0.93/0.43 |
| ĐÍCH DK2-1 | 0..2.62 | 1.35,1.31,1.24,0.78 | 2.20/1.57/1.05/0.43 |

**KẾT QUẢ FIX 08:28 (căn cứ):**
- ✅ Crop nới 2.14→3.08 (fix CrossHeightMarginFeet chạy).
- ✅ Head bám thép TỐT HƠN: bản cũ head-thép lệch tới 0.69; nay TB ~0.35 (head 1.43 vs thép 1.46 gần khít).
- Head gap đều 0.5 (min-gap) vì 3 thép trên chụm (1.38-1.49) → head phải giãn. Đúng hành vi mong muốn.
- → leader gọn hơn hẳn, khớp cấu trúc đích. Nếu user OK → finalize.

## Lần đo 2026-07-02 08:45 — MRA dim line (tìm gốc leader MRA gập)
Leader MRA thép dọc = do MultiReferenceAnnotation tự vẽ (dim + tag). Đo:
| MRA | ADDIN head Y / dimSegs | ĐÍCH head Y / dimSegs |
|---|---|---|
| trên | 1.93 / **2 (chain)** | 2.2 / 2 (chain) |
| giữa | 0.93 / 0 (122mm single) | 1.57 / 0 (122mm single) |
| dưới | 0.43 / 2 (chain) | 0.43 / 2 (chain) |

**CĂN CỨ:** cấu trúc MRA GIỐNG đích (2 chain + 1 single). Leader MRA "gập" là do **dim dạng CHAIN 2 segment**
(nối nhiều thanh thép → đường có điểm gãy) — BẢN CHẤT của MultiReferenceAnnotation, ĐÍCH CŨNG CÓ. Khác biệt: thép
addin CHỤM (chain ngắn, góc gãy gắt) vs thép đích TRẢI RỘNG (chain dài, nhìn thoải). Head/leaderEnd/tag type đều KHỚP.
→ Không sửa được góc chain bằng head position. Muốn leader 1 đoạn thẳng: (a) đổi MRA type sang loại KHÔNG chain,
hoặc (b) bỏ MRA, mỗi thanh thép 1 IndependentTag riêng (mất dim thép tự sinh). Cần user quyết.

## Script MCP đo lại (dán vào send_code_to_revit, biến `document`)
```csharp
System.Func<string,ViewSection> latest = pre => new FilteredElementCollector(document).OfClass(typeof(ViewSection))
  .Cast<ViewSection>().Where(x=>!x.IsTemplate&&x.Name.StartsWith(pre)).OrderByDescending(x=>x.Id.Value).FirstOrDefault();
foreach(var v in new[]{ latest("MCN-DK2-GOI"), latest("MCN-DK2-NHIP") }){
  var cb=v.CropBox; var inv=cb.Transform.Inverse;
  System.Diagnostics.Debug.Print(v.Name+" cropY=[0.."+System.Math.Round(cb.Max.Y,2)+"]");
  foreach(var t in new FilteredElementCollector(document,v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
    { var h=inv.OfPoint(t.TagHeadPosition); /* h.X,h.Y + type=document.GetElement(t.GetTypeId()).Name */ }
}
```
