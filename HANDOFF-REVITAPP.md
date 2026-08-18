# HANDOFF REVITAPP

## Thay đổi đang chờ phát hành v1.14.2

- Root cause đăng nhập v1.14.1: GitHub Actions secret có một ký tự ẩn `U+FEFF` ở đầu; DLL nhúng 65 ký tự trong khi secret chuẩn là Base64URL 64 ký tự, nên Apps Script trả `unauthorized_v2` dù OAuth Google thành công.
- Workflow mới chỉ chấp nhận đúng 64 ký tự Base64URL và sinh source UTF-8 không BOM. Job Add-in xác minh `RevitAPP.dll` repack cuối; job Installer xác minh DLL `RevitAPP.Licensing` trung gian dùng để publish Installer. Hai bước đều không in giá trị/hash, và cả Add-in R22–R27 lẫn standalone Installer đều phải nhúng secret.
- Kiểm tra/cài cập nhật không còn bị license gate chặn, để bản lỗi hoặc hết hạn vẫn có đường tự cập nhật. Callback trình duyệt chỉ báo đã xác thực Google, chưa tuyên bố license thành công.
- Apps Script phải dùng `String(body.secret || '').replace(/^\uFEFF/, '')` trước phép so sánh để cứu v1.14.0/v1.14.1 đang cài. Chỉ bỏ đúng một BOM đầu chuỗi, không `trim()` secret.
- Gate local hiện đã đạt 600/600 test (`RevitAPP.Licensing.Tests` 18/18 và `RevitAPP.Tests` 582/582); build Release R22–R27 và publish Installer đều thành công.
- **Đang chờ:** production Apps Script Web App chưa được xác nhận đã deploy bản tương thích BOM. Không tag/phát hành v1.14.2 cho tới khi endpoint production trả cùng kết quả xác thực cho secret chuẩn và secret có đúng một BOM đầu chuỗi.
- GitHub secret đã được ghi lại từ Windows **User scope** (không dùng process env cũ). Sau khi Apps Script production được deploy và xác minh, chạy lại release gate trên đúng cây public trước khi tag v1.14.2.

## Phát hành v1.14.1

- License đã chuyển sang **server-authoritative**: mọi lần chạy lệnh được bảo vệ đều gọi Apps Script; cache `%AppData%\RevitAPP\license.json` chỉ nhớ email/trạng thái và không cấp quyền. Đổi hạn, thu hồi hoặc gia hạn trên Google Sheet có hiệu lực ở lần bấm lệnh kế tiếp mà khách không cần đăng xuất.
- Mất mạng/timeout phải **fail closed** để không thể dùng license đã bị thu hồi. Đây là đánh đổi bắt buộc của yêu cầu cập nhật tức thì.
- Không được khôi phục `CacheGraceDays=7` hoặc nhánh `Allowed && IsWithinGrace(...)`. `CacheGraceDays=0` chỉ còn để giữ tương thích chữ ký constructor cũ.
- Không kiểm tra ngày hết hạn cũ trong cache trước server: server phải được phép gia hạn một cache đã hết hạn mà không bắt khách đăng nhập lại.
- Ghi cache dùng named mutex liên tiến trình + file tạm GUID + `File.Replace`; lỗi ghi cache là best-effort và không được chặn lệnh nếu server vừa trả `allowed=true`.
- Cache luôn có `sessionId/generation`, kể cả trạng thái đã đăng xuất (tombstone có `email=null`). Kết quả verify/sign-in chỉ được ghi nếu generation ban đầu vẫn khớp dưới cùng mutex với `Clear`. Vì vậy đăng xuất khi request đang chờ luôn thắng, response cũ không thể ghi đè lần đăng nhập mới cùng email, và first sign-in cũng không thể hồi sinh sau SignOut.
- `ReadOrCreateSessionSnapshot()` phải migrate nguyên tử cache legacy chưa có `sessionId`; cache JSON hỏng/không tồn tại được thay bằng tombstone hợp lệ trước khi gọi mạng. Không quay lại mô hình xóa file khi SignOut vì cặp `(absent → absent)` không thể nhận biết generation đã đổi.
- Test hồi quy nằm trong `tests/RevitAPP.Licensing.Tests/LicenseServiceTests.cs`: thu hồi khi cache mới, offline fail closed, gia hạn cache hết hạn, cache-write failure, concurrent writes, hai race SignOut, first sign-in, cache JSON hỏng và hai verify đồng thời trên cache legacy. Trước fix ba test cốt lõi thất bại; sau redesign bộ License đạt 17/17.
- Gate trước phát hành: `RevitAPP.Licensing.Tests` 17/17 và `RevitAPP.Tests` 582/582 đạt (tổng 599/599); build Release R22–R27 thành công với `DeployAddin=false`, `LaunchRevit=false`. Không đóng/mở Revit trong quy trình phát hành.
- GitHub Actions run `32114497357` phát hành thành công: đủ 8 asset, `latest.json` trả HTTP 200 với version `1.14.1`; SHA-256 gói R25 khớp manifest và SHA-256 installer nhúng trong gói R25 khớp installer độc lập.

## Phát hành v1.14.0

- `v1.14.0`: bổ sung REVIEW 3D tương tác cho Vẽ Móng Đơn. Hình bê tông lấy từ đúng móng được chọn; lưới đáy/trên/giữa, chân chó và đai ngang có màu riêng, cập nhật trực tiếp theo mọi tùy chọn. Móng tam giác và đa giác bất kỳ dùng đường thép được cắt/inset theo tiết diện thật; Preview và Create dùng chung đường tâm thanh, bảo đảm lớp bảo vệ gồm bán kính thanh. Bê tông hiển thị bán trong suốt để nhìn thấy thép bên trong.
- An toàn cấp phép: OAuth desktop dùng PKCE và không nhúng client secret; shared secret lấy từ Script Properties và GitHub Actions secret, không còn ghi cứng trong source phát hành.
- Gate local trước phát hành: 635/635 test đạt; build Release R22–R27 thành công. Không đóng hoặc tự mở Revit trong quy trình phát hành.
- GitHub Actions run `32110256687` xanh toàn bộ: Installer, build R22–R27 và Release đều thành công. Release có đúng 8 asset; `latest.json` trả HTTP 200 với version `1.14.0`; SHA-256 gói R25 khớp manifest và `RevitAPP.Installer.exe` nhúng trong gói R25 cũng khớp SHA-256 của Installer độc lập.

### Vận hành License Apps Script

- Web App production dùng endpoint `/exec` đã cấu hình trong `LicenseConfig`; không ghi URL triển khai cụ thể vào handoff hoặc log.
- Shared secret không được ghi vào source hoặc handoff. Giá trị hiện được lưu ở ba nơi: Apps Script Project Settings > Script Properties, GitHub Actions secret của `linhldcs-design/REVIT-APP02`, và biến môi trường Windows User `REVITAPP_LICENSE_SHARED_SECRET` trên máy phát hành.
- Tên thuộc tính/secret ở cả ba nơi phải là `REVITAPP_LICENSE_SHARED_SECRET`.
- Apps Script phải đọc bằng `PropertiesService.getScriptProperties().getProperty('REVITAPP_LICENSE_SHARED_SECRET')`; không khai báo chuỗi secret trực tiếp trong `Mã.gs`.
- Khi cập nhật Apps Script: chọn **Triển khai > Quản lý hoạt động triển khai**, chọn đúng loại **Ứng dụng web** có URL kết thúc bằng `/exec`, bấm bút chì, chọn **Phiên bản mới**, rồi **Triển khai**. Không triển khai nhầm loại **Thư viện** (`/macros/library/...`) vì thao tác đó không cập nhật endpoint của ứng dụng.
- Kiểm tra an toàn sau triển khai: POST một email giả cùng secret lấy từ biến môi trường. Kết quả `not_found` nghĩa là secret được chấp nhận và endpoint hoạt động; `unauthorized` nghĩa là secret/phiên bản Web App chưa khớp. Không in request body hoặc secret vào log.
- Đồng bộ GitHub bằng cách pipe biến môi trường vào `gh secret set REVITAPP_LICENSE_SHARED_SECRET --repo linhldcs-design/REVIT-APP02`; sau đó chỉ kiểm tra tên secret bằng `gh secret list`, không đọc giá trị.
- Nếu xoay secret: tạo ngẫu nhiên tối thiểu 32 byte, cập nhật Apps Script Property trước, triển khai Web App mới, kiểm tra endpoint đạt, rồi cập nhật GitHub secret và biến môi trường local. Không chụp màn hình ô giá trị.

## Câu lệnh bắt đầu cho AI mới

> Đọc toàn bộ `HANDOFF-REVITAPP.md`, kiểm tra trạng thái Git và tiếp tục công việc hiện tại. Không đóng hoặc mở Revit nếu người dùng chưa cho phép.

## Dự án và phát hành

- Thư mục làm việc: `C:\Users\Admin\OneDrive\Desktop\RevitAI`
- Repository: `https://github.com/linhldcs-design/REVIT-APP02`
- Nhánh phát hành: `main`
- Repository đang Public để Installer tải Release không cần đăng nhập GitHub.
- Release mới nhất đang phát hành: `v1.14.1`
- Workflow: `.github/workflows/release-revitapp.yml`
- Installer trên Desktop: `C:\Users\Admin\Desktop\RevitAPP-Installer\RevitAPP.Installer.exe`
- Installer đã cài: `%LocalAppData%\Programs\RevitAPP Installer\RevitAPP.Installer.exe`

## Cơ chế cập nhật

- Installer và Add-in đọc:
  `https://github.com/linhldcs-design/REVIT-APP02/releases/latest/download/latest.json`
- Mỗi Release phải có:
  - `RevitAPP.Installer.exe`
  - `latest.json`
  - `RevitAPP-R22-<version>.zip`
  - `RevitAPP-R23-<version>.zip`
  - `RevitAPP-R24-<version>.zip`
  - `RevitAPP-R25-<version>.zip`
  - `RevitAPP-R26-<version>.zip`
  - `RevitAPP-R27-<version>.zip`
- License và preset người dùng phải được giữ nguyên khi cập nhật.

## Trạng thái kỹ thuật

- RevitAPP đã build thành công cho Revit 2022–2027.
- Revit 2022 có fallback cho `Viewport.GetProjectionToSheetTransform`.
- Revit 2022–2024 bỏ qua Rebar Bending Detail vì API chưa hỗ trợ.
- Test gần nhất: `RevitAPP.Tests` 582/582 và `BeamRebarPro.Tests` 128/128 đạt.
- Chat AI có registry **57 tool duy nhất** và vẫn hoạt động từ cửa sổ Chat trên Ribbon. Cùng registry đó được mở thêm qua MCP Streamable HTTP tích hợp tại `http://127.0.0.1:8765/mcp`; không cần `revit_mcp_plugin`, `commandRegistry` hay MCP server ngoài. Cả Chat AI và MCP dùng chung `ExternalEvent`, license gate và transaction ownership hiện có.
- Build xác nhận gần nhất: đủ Revit 2022–2027 đều thành công; bản Revit 2025 đã được triển khai thực tế.
- `send_code_to_revit` giới hạn tối đa 1.200 ký tự và luôn yêu cầu người dùng xác nhận trước khi chạy C#.
- Bộ lọc màu đã được sửa; thao tác tạo kích thước chạy nguyên tử, lỗi giữa chừng không để lại kết quả dở dang.
- GitHub Actions của Release `v1.1.5`: thành công toàn bộ; Release có đủ 8 asset và `latest.json` trả HTTP 200.
- `v1.1.6`: native hóa 21 MCP proxy để đủ 53 tool chạy độc lập trên máy đích; không cần `revit_mcp_plugin`, `commandRegistry.json` hoặc MCP server. Bổ sung chọn toàn bộ tag cột trong view bằng `OST_StructuralColumnTags`, chạy trực tiếp không cần API key.
- GitHub Actions Release `v1.1.6` (run `29736265914`) thành công; 8 assets đã phát hành và `latest.json` v1.1.6 trả HTTP 200.
- `v1.2.0`: thêm lệnh triển khai mặt cắt dọc dầm theo một chuỗi dầm liên tục, preview trục/vị trí cắt, mặt cắt ngang gối–nhịp, tag/dim/detail thật và phát hành lên sheet có sẵn. Kiểm thử local đạt 196 + 15 test; build Release R22–R27 thành công.
- `v1.2.1`: thiết kế bộ icon riêng nền trong suốt cho toàn bộ lệnh; tách Ribbon thành `Commands`, `Rebar` và `Drawing Rebar`; đổi tên `Bản Vẽ Dầm` thành `Mặt Cắt Ngang Dầm`, `Bản Vẽ Móng` thành `Mặt Bằng Móng`.
- `v1.2.2`: đổi tên tab Ribbon hiển thị từ `RevitAPP` thành `LDL-STRUCTURAL`; giữ nguyên assembly, manifest, namespace, pack URI và toàn bộ logic lệnh để bảo đảm tương thích cập nhật.
- `v1.2.3`: hoàn thiện bộ icon nền trong suốt cho các lệnh trên Ribbon.
- `v1.2.4`: sửa lệnh Mặt Cắt Ngang Dầm chỉ sắp xếp viewport vừa tạo; giữ nguyên viewport cũ trên sheet, tránh nội dung hiện hữu và vẫn giữ viewport mới để người dùng chỉnh tay khi không còn vùng trống.
- `v1.3.0`: thêm 2 Chat AI tool tìm preset và triển khai mặt cắt dọc dầm hàng loạt lên sheet có sẵn. Hỗ trợ số dầm mỗi sheet do người dùng chọn, chia dependent view tại lưới gần trung điểm, đặt nét cắt cách lưới ưu tiên 500 mm, xếp mặt cắt ngang 1–2 hàng và luôn giữ nguyên tỷ lệ. Khi sheet thiếu chỗ, tool vẫn đặt view để người dùng tiếp tục sắp xếp tay thay vì rollback vì kích thước.
- `v1.3.1`: thêm lệnh `Lưới 3D/2D` trong panel `Commands` để đồng bộ cả hai đầu của toàn bộ lưới trục đang hiển thị trong mặt bằng. Nếu còn đầu lưới ở 3D, lệnh chuyển toàn bộ sang 2D; khi tất cả đã ở 2D, lệnh chuyển ngược lại 3D. Mỗi lưới được xử lý trong sub-transaction riêng và lưới không thể chỉnh sửa được bỏ qua có báo cáo.
- `v1.3.2`: mở rộng lệnh `Lưới 3D/2D` để sử dụng trong mặt đứng, mặt cắt và Detail/Callout dạng `ViewSection`, ngoài mặt bằng; thông báo kết quả dùng tên view hiện tại.
- `v1.4.0`: phát hành toàn bộ 55 Chat AI tool qua MCP Streamable HTTP tích hợp, đồng thời giữ nguyên cửa sổ Chat AI. Endpoint loopback yêu cầu bearer token 256-bit tại `%LocalAppData%\RevitAPP\mcp-access-token.txt`, dùng MCP ổn định `2025-11-25`, hàng đợi worker có giới hạn, kết quả được liên kết riêng theo từng request và mọi tool thay đổi model đều yêu cầu xác nhận trong Revit; license gate và transaction ownership hiện có được giữ nguyên.
- `v1.5.0`: thêm lệnh `Xuất DWG Model` trong panel `CAD Tools`. Lệnh chọn DWG Export Setup, phiên bản DWG và Print Set đã lưu; xuất toàn bộ sheet thành một DWG Model Space tự chứa qua AutoCAD 2024 Automation riêng. DIM và DIMSTYLE được chuyển annotative, Text Style dùng Arial Narrow cao 2.5 và width factor 0.8, giữ DIMLFAC theo viewport; worker có ownership/timeout/retry và được đóng gói self-contained trong mỗi gói R22–R27. Runtime smoke Revit 2025 đạt 34/34 sheet, 1.695/1.695 DIM và Text Style đúng contract, `AUDIT` 0 lỗi.
- `v1.5.1`: sửa workflow clean-run để restore `DwgExportWorker` trước khi publish; `v1.5.0` chỉ là tag build thất bại và không tạo GitHub Release.
- `v1.5.2`: chuẩn hóa DIMLFAC cho mọi sheet có viewport, kể cả sheet chỉ có một tỷ lệ. View tham chiếu (mẫu số tỷ lệ lớn nhất trên sheet) luôn có DIMLFAC = 1; các view còn lại dùng `view.Scale / referenceScale` (ví dụ 1:25 so với 1:75 là 25/75). `v1.5.1` đã bị hủy trong lúc chạy workflow và không tạo GitHub Release vì phát hiện lỗi này trước khi phát hành.
- `v1.5.3`: sửa đầu DIM/tick bị nhỏ sau khi chuyển annotative. Revit ghi override DIMASZ theo inch; worker chuyển riêng từng override sang đơn vị DWG đích sau ANNOUPDATE để giữ đúng kích thước riêng của từng kiểu đầu DIM mà không làm chậm bước regenerate. Kiểm chứng trực tiếp 1.695/1.695 DIM: DIMASZ đổi đúng inch sang mm, giữ nguyên REVIT XData và trạng thái annotative.
- `v1.6.0`: thêm lệnh `Tạo Lưới từ Cad` cho Revit 2025 tại `LDL-STRUCTURAL > CAD Tools`. Người dùng chạy lệnh trong Revit, quét chọn thủ công các đối tượng `LINE` trong AutoCAD 2024–2027 đang mở, xem/chọn/chỉnh tên các trục trong preview rồi bấm một điểm gốc đặt lưới. Add-in giữ nguyên khoảng cách, góc và chiều dài tương đối của line CAD, hỗ trợ cả trục chéo, bỏ qua Grid trùng và tạo toàn bộ Grid mới trong một transaction; không cần CAD link/import hoặc hai Grid neo có sẵn.
- `v1.7.0`: lệnh `Dịch Text` không còn yêu cầu API key và có thể dịch TextNote trong các Viewport được chọn. Chat AI/MCP bổ sung `get_viewport_text_notes` và `apply_text_note_translations` để dịch song ngữ Việt/Trung cho TextNote, tên hiển thị Viewport và Sheet Name trên toàn bộ project; ưu tiên `Title on Sheet` khi có nội dung, chỉ dịch `View Name` khi trường này rỗng, luôn giữ nguyên `Sheet Number`, hỗ trợ phân trang và kiểm tra nội dung gốc trước khi ghi để tránh ghi đè thay đổi mới.
- `v1.8.0`: mở rộng CAD Tools thành cửa sổ `Model From CAD` có hai tab `Create Grid` và `Create Column`. Lệnh quét LINE, closed polyline và nested block từ AutoCAD, dùng điểm móc nguồn/đích, preview 2D/3D có zoom và orbit, chọn family/tham số b-h/level/offset/rotation rồi tạo Structural Column theo transaction. Bổ sung kiểm tra trùng cấu hình, xác nhận rectangle từ bốn LINE rời, giới hạn entity/thời gian đọc và lazy-render để tránh treo khi vùng CAD lớn.
- `v1.9.0`: thêm tab `Create Beam` vào `Model From CAD`. Workflow quét hai bước `Grid Axes` rồi `Beam Lines`; đọc tiết diện `b×h` từ TEXT/MTEXT; ghép các boundary rail bị đứt thành một cây dầm theo tùy chọn `Gap Join`; giữ các đoạn liên tục trên cùng trục và chỉ tách khi `b×h` đổi hoặc khi khe vượt `Gap Max`; cho chỉnh tiết diện trong Review 2D/3D có zoom/orbit; và tạo `Structural Framing` trong Revit. Kiểm thử tự động đạt 390/390; build R22–R27 thành công.
- Các lỗi đã sửa trong `v1.9.0` sau khi người dùng thử trên bản vẽ thật: đọc selection AutoCAD không còn giải phóng COM giữa vòng lặp nên không bỏ sót entity; biên lệch góc hoặc lệch offset vài mm vẫn ghép thành một dầm; biên bị trim vụn tại mặt cột vẫn dựng đủ chiều dài; polyline có một cạnh cong vẫn giữ các cạnh thẳng; nhãn tách làm hai đối tượng TEXT vẫn đọc được Mark; dầm bị nhánh cắt ngang không còn mất nhãn; và dầm mà bản vẽ chỉ ghi nhãn một lần vẫn được giữ.
- **Runtime smoke của tab `Create Beam` chưa chạy khi phát hành `v1.9.0`.** Người dùng yêu cầu phát hành trước khi kiểm chứng lại trên Revit. Lần mở phiên sau cần quét thử `Grid Axes` + `Beam Lines` trên bản vẽ thật và kiểm tra dòng cảnh báo `N đường biên không ghép được thành dầm` dưới bảng review.
- `v1.10.0`: thêm tab `Create Slab` vào `Model From CAD`. Workflow bốn bước `Grid Axes` → `Slab Lines` → `Ô Trống` → `Vùng Hatch`; biên sàn tính một lần từ toàn bộ line đã quét rồi làm phẳng qua cột; vùng hatch là sàn hạ riêng, cắt khỏi sàn chính theo đúng đường bao của nó; hai mảng hatch chỉ cách nhau một dầm thì nối, cách xa thì tách; ô trống chỉ nhận từ outline người dùng pick; chiều dày và cao độ đọc từ TEXT/MTEXT. Kiểm thử tự động đạt 449/449; build R22–R27 thành công.
- Các lỗi đã sửa trong `v1.10.0` sau khi người dùng thử trên bản vẽ thật: hatch nhỏ hơn ô lưới không còn biến mất; hatch không nuốt bay không tô bên cạnh; sàn chính dừng ở mép sàn hạ thay vì đổ đè lên; lỗ nằm ngoài hoặc chạm biên bị bỏ riêng thay vì làm hỏng cả profile; biên chạy thẳng qua cột thay vì răng cưa; khe giữa hai vùng hatch được lấp vuông góc thay vì nối chéo; vùng không hatch là một tấm một cao độ và chỉ tách khi thật sự không liên thông; nhãn chỉ lan trong cùng loại vùng; dải dầm chỉ đọc nhãn từ bay cùng loại vùng — trước đó dải cạnh vùng hatch kéo cao độ sàn hạ ra khắp sàn.
- **Runtime smoke của tab `Create Slab` chưa được người dùng xác nhận đầy đủ khi phát hành `v1.10.0`.** Bản R25 đã deploy và người dùng thử nhiều vòng; lần sửa cuối (`8008c28`) chưa có phản hồi. Lần mở phiên sau cần kiểm tra bảng review có ra đúng cao độ CAD không, và thanh trạng thái có dòng `Cao độ đọc được` / `Chiều dày đọc được` để chẩn đoán.
- `v1.10.1`: sửa hai lỗi của lệnh `Xuất DWG Model` trên máy khách. Máy có AutoCAD vẫn báo "Không tìm thấy AutoCAD đầy đủ" vì add-in dò bằng danh sách ProgId cứng chỉ có 2024–2027; nay bổ sung 2016–2023 và dò thẳng `HKEY_CLASSES_ROOT` để nhận mọi bản đã đăng ký Automation, kể cả bản phát hành sau này. Lệnh `_.SCRIPT` gửi sang AutoCAD mà không tắt `FILEDIA` nên AutoCAD mở hộp thoại `Select Script File` và chờ người dùng bấm; nay tắt `FILEDIA` trước khi gửi, khôi phục lại sau kể cả khi lỗi, và ghi đường dẫn bằng dấu `/`. Thông báo khi thiếu AutoCAD nói rõ cần bản đầy đủ 2016 trở lên, AutoCAD LT và Revit không thay thế được.
- Sửa kèm `v1.10.1`: `FootingDrawing.Addin` không còn khai báo extension `ToLong` riêng. `Nice3point.Revit.Extensions` cung cấp sẵn cho mọi phiên bản đích, nên bản trùng tên làm build local hỏng `CS0121` ở R22–R27.
- `v1.11.0`: `Create Slab` đọc được đường cong và dựng đúng biên sàn trên bản vẽ thật. Bổ sung đọc `ARC` vẽ riêng (đi theo tâm và bán kính của chính nó, 5° một bước — dựng lại từ dây cung sai tới 1,6 m với cung nông trên dây dài) và cạnh cong trong polyline qua bulge (`CadArcChords`, 30 test). Ô trống pick nhận cả rectangle, closed polyline và line rời: mỗi pick đi theo riêng nên góc lệch vài mm vẫn khép. Thêm tùy chọn `Ô trống min` (m²) bỏ qua pick quá nhỏ. Kiểm thử 482/482; build R22–R27 thành công.
- Các lỗi đã sửa trong `v1.11.0`: sàn không còn khoét lỗ tại cột, hố thang hay bay biên hở — chỉ khoét ô người dùng pick và sàn hạ; biên chạy thẳng qua cột thay vì răng cưa (vết khía nhiều đỉnh trước đây không khớp vì so nhầm hai cạnh của chính đầu dầm thay vì hai đoạn biên hai bên); biên giữ góc vuông nơi bản vẽ vẽ vuông, không nối chéo; trục lưới thò ra ngoài công trình không còn kéo méo biên — mỗi line bị cắt về đoạn giữa hai điểm cắt ngoài cùng, phần đuôi không bao quanh ô nào thì không định hình biên.
- **Bài học `v1.11.0`:** một bộ lọc theo tên layer (`GRID`, `DIM`, `TEXT`) từng được thêm rồi phải `git revert` — trục lưới nằm trên `S-GRID` nên bước `Select Grid Axes` không đọc được gì. Tên layer không nói lên công dụng của line; hình học thì có.
- **Bài học `v1.11.0`:** công thức cung tròn ban đầu được dò dấu bằng script rời, tám vòng không hội tụ. Viết `CadArcChordsTests` (30 ca: nửa tròn, 1/4, cung nông, cung lớn, cạnh xiên, cả hai chiều) trước rồi mới sửa — ba lần là xanh.
- `v1.11.1`: `Create Slab` tự sinh Floor Type đúng chiều dày CAD ghi. Cơ chế nhân bản đã có từ `v1.10.0` nhưng tìm nhầm type: mọi Floor Type trong Revit đều có `FamilyName = "Floor"`, nên điều kiện `FamilyName == seed.FamilyName` khớp với bất kỳ type nào cùng chiều dày — chọn sàn bê tông có thể lấy nhầm sàn metal deck. Nay hai type là một khi cấu tạo giống nhau (cùng số lớp, chức năng lớp, vật liệu, thứ tự); chiều dày khác là thứ đang được thay đổi. Tên type sinh ra lấy theo type gốc thay vì family: `160mm Concrete With 50mm Metal Deck (210 mm)` sinh ra `Concrete With Metal Deck 120mm`, không còn `Floor 120`. Thêm `CadSlabTypeNaming` + 13 test (gồm ca sao chép hai lần không được thành `Concrete 150mm 200mm 250mm`). Kiểm thử 495/495; build R22–R27 thành công.
- `v1.12.0`: thêm tab `Create Wall` trong `Model From CAD`, nhận tường từ hai biên/rectangle theo layer, review 2D/3D và dựng Wall theo Level/Wall Type; đồng thời nâng cấp `Vẽ Thép Cột` với review 2D/3D có level, hình học dùng chung với Revit, thép móng chạy liên tục và so le, nối thép theo nguyên tắc 2, chuyển tiết diện, móc/uốn, đai một khoảng cách duy nhất và giao diện responsive. Preset/license người dùng được giữ nguyên khi cập nhật. GitHub Actions run `31571123045` thành công; đủ 8 asset và `latest.json` v1.12.0 trả HTTP 200.
- `v1.13.0`: `Vẽ Thép Dầm` có khung xem trước 2D/3D trong cả Quick Setting và Detail Rebar Forms, dựng từ cùng một mô tả hình học với thép được tạo trong Revit. Hình học tách thành factory thuần trong `RevitAPP.Core`: `RebarLayoutMath` nhân bản thanh theo đúng ngữ nghĩa `SetLayoutAsMaximumSpacing`/`SetLayoutAsFixedNumber` (bước co lại để phủ hết vùng, không giữ bước danh nghĩa rồi hở ở cuối), `PureSpanFrame` dựng hệ trục nhịp độc lập Revit. Builder giữ nguyên cơ chế layout của Revit nên thép tạo ra không đổi. Bản xem trước dùng tiết diện, cao độ và toạ độ thật của dầm đang chọn nên khớp cả khi dầm nằm xiên. Kiểm thử 710/710 đạt; build R22–R27 thành công.
- Các lỗi đã sửa trong `v1.13.0` sau khi người dùng thử trên mô hình thật: thanh chủ bị cắt tại gối giữa (thực tế chạy suốt cả dầm vật lý); thép gia cường trên vẽ thành hai cây bẻ móc tại cột giữa thay vì một cây chạy xuyên — quy tắc thật là mỗi gối một thanh vắt qua, chỉ hai gối biên mới có móc (`DLeftMm = support == 0 ? … : 0`); đoạn bẻ đầu thép chủ bỏ qua `Anchor Left/Right` mà chỉ đọc `Bend down`, và thép dưới bẻ ngược lên chứ không xuống; đai phụ chưa hề được dựng; màn Detail đọc cấu hình gộp thay vì `TopAdditionalItems`/`BottomAdditionalItems` nên mọi thao tác thêm/xoá/sửa trong Detail không hiện ra.
- **Bài học `v1.13.0`:** bốn vòng sửa đầu cho lỗi "Detail chưa ăn" đều đoán sai (dispatcher, nguồn nhịp, ô nhập, thẻ ẩn) vì add-in tạo logger nhưng không gán `Log.Logger`, nên mọi lời gọi ghi nhật ký rơi vào hư không và không có gì để lần. Sau khi ghi nhật ký ra file tại `%LocalAppData%\BeamRebarPro\logs`, log chỉ ra ngay trong một vòng: dữ liệu đúng, khung vẽ không được gọi. Bật ghi nhật ký trước khi suy đoán.
- **Bài học `v1.13.0`:** chỉ verify `Debug.R25` suốt quá trình phát triển nên `Math.Clamp` và `Split(char, StringSplitOptions)` lọt vào `RevitAPP.Core` — cả hai chỉ có từ .NET Core, làm R22–R24 (net48) build hỏng ở bước 4 của quy trình phát hành. `RevitAPP.Core/Services/MathCompat.cs` đã có sẵn polyfill cho đúng việc này.
- **Runtime smoke `v1.13.0`:** người dùng đã xác nhận khung xem trước chạy đúng trên Revit 2025 trước khi phát hành.
- `v1.13.1`: sửa Installer trên máy khách phải bấm cập nhật/đóng mở nhiều lần. Tất cả thao tác mạng có retry giới hạn, UI khóa đăng nhập/chọn phiên bản/nút cập nhật khi đang bận, năm Revit được chụp cố định trước các bước bất đồng bộ, và payload add-in được thay theo staging + rollback để không để lại bản cài dở. Mỗi ZIP R22–R27 nay nhúng cùng `RevitAPP.Installer.exe`; khách đang dùng Installer cũ chỉ cần cập nhật add-in một lần rồi mở Revit để cầu nâng cấp thay Installer đã cài. Từ lần sau Installer đọc package riêng trong `latest.json`, kiểm tra SHA-256, tự thay chính nó và phục hồi EXE cũ nếu không mở được bản mới. License/preset vẫn nằm ngoài payload và được giữ nguyên. Kiểm thử 590/590 đạt; build R22–R27 và Installer đơn-file thành công. GitHub Actions run `31987499500` xanh toàn bộ; Release đủ 8 asset, `latest.json` trả HTTP 200 và gói R25 đã được kiểm tra có Installer 1.13.1.0 với SHA khớp manifest.
- Thay đổi cho `v1.0.1`: xóa nút `Cap Nhat` khỏi Ribbon; Installer vẫn kiểm tra cập nhật.
- Thay đổi cho `v1.0.2`: thêm tùy chọn bẻ móc thép tường vào trong/ra ngoài độc lập cho đầu trên và dưới; bản Debug không tự thay bằng Release khi khởi động.
- Thay đổi cho `v1.1.0`: thêm Chat AI 47 tool, trí nhớ mã hóa, điều khiển toàn bộ nút RevitAPP và đọc Excel.
- Thay đổi cho `v1.1.1`: sửa chọn toàn bộ phần tử bằng tool native và áp dụng license gate cho mọi nút chức năng RevitAPP; nút License vẫn mở để kích hoạt/gia hạn.
- Thay đổi cho `v1.1.2`: Chat AI hỗ trợ chọn ảnh, dán ảnh từ clipboard và kéo thả ảnh; ảnh được chuẩn hóa trước khi gửi và chuyển đúng định dạng vision cho OpenAI, Anthropic và Gemini.
- Thay đổi cho `v1.1.3`: phát hành Chat AI 49 tool; sửa Gemini tool schema; đọc bảng Excel đang mở đúng cả khi UsedRange không bắt đầu tại A1; vẽ dầm theo Instance Mark và cấu hình Excel; gọi hệ cột theo Instance Mark/cấu hình add-in; giảm số lần Regenerate để tránh lag; không báo thành công khi không tạo được thép.
- Thay đổi cho `v1.1.4`: công cụ Vẽ Móng Đơn bỏ qua solid bê tông lót ở dưới cùng khi đọc hình học family; ưu tiên Material/Subcategory và có nhận dạng hình học dự phòng cho family không gán metadata.
- Thay đổi cho `v1.1.5`: Chat AI có 53 tool; thêm vẽ mặt bằng/mặt cắt móng trực tiếp và điều phối C# nguyên tử để giữ đúng viewport ID, xếp mặt bằng trên/mặt cắt dưới, căn tên view, kiểm tra sức chứa/va chạm nội dung sheet và rollback toàn bộ khi lỗi.
- Các commit phát hành gần nhất:
  - `72012ff` — Chat AI 53 tool và tự động triển khai bản vẽ móng lên sheet; phát hành v1.1.5
  - `dd37339` — bỏ qua bê tông lót khi đọc hình học móng đơn; phát hành v1.1.4
  - `aa1d25a` — attach installer executable to releases
  - `37f6391` — publish standalone installer in releases
  - `eb200ef` — release RevitAPP installer for Revit 2022-2027

## Quy trình khi người dùng nói “PHÁT HÀNH BẢN MỚI”

1. Đọc yêu cầu và kiểm tra `git status -sb`; không stage file không liên quan.
2. Sửa code và kiểm tra không làm mất thay đổi của người dùng.
3. Chạy test phù hợp, tối thiểu:
   `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release`
4. Build đủ sáu bản với `DeployAddin=false` và `LaunchRevit=false`:
   `dotnet build RevitAPP/RevitAPP.csproj -c Release.R22 -p:DeployAddin=false -p:LaunchRevit=false`
   và lặp lại cho R23, R24, R25, R26, R27.
5. Tăng version theo SemVer; không dùng lại tag đã tồn tại.
6. Commit có chủ đích và push lên `origin/main`.
7. Tạo tag, ví dụ `v1.0.1`, rồi push tag để chạy workflow.
8. Theo dõi GitHub Actions đến khi job `installer`, sáu job build và job `release` đều thành công.
9. Kiểm tra Release có đủ tám asset liệt kê phía trên.
10. Kiểm tra URL `releases/latest/download/latest.json` trả HTTP 200.

## Lưu ý an toàn

- Không chạy build với deploy/launch mặc định vì có thể mở hoặc khóa Revit.
- Không đóng Revit nếu người dùng chưa yêu cầu.
- Không commit `bin`, `obj`, `artifacts`, bundle MCP, file tạm hoặc cấu hình local.
- Worktree có một số thư mục chưa tracked thuộc công việc khác; không tự ý `git add -A`.
- Không hiển thị OAuth client, shared secret hay token trong log hoặc câu trả lời.
