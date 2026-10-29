# Nesting2D — hướng dẫn kiến trúc và luồng hoạt động

> Tài liệu dành cho lập trình viên mới và AI coding agent tiếp nhận dự án. Hãy đọc tài liệu này cùng `README.md`; README ưu tiên hướng dẫn người dùng, tài liệu này giải thích cấu trúc và luồng nội bộ.

## 1. Mục tiêu và phạm vi

Nesting2D là ứng dụng desktop Windows dùng WPF để đọc hình học chi tiết từ DXF ASCII, chuẩn hóa về mm, tìm cách bố trí các chi tiết trên các khổ tấm, cho phép điều chỉnh thủ công, kiểm tra độc lập kết quả và xuất DXF/PDF/báo cáo. Thuật toán lõi và UI Core nhắm `.NET 8`; Desktop nhắm `net8.0-windows` và phụ thuộc WPF.

Điểm quan trọng về miền nghiệp vụ: với chi tiết DXF, hệ thống phải dùng contour thật. Nếu contour ngoài không thể xác minh thì phải báo lỗi và từ chối nesting; không được lặng lẽ thay bằng hình chữ nhật hộp bao. Hộp bao được dùng cho kích thước, vùng lọc, giới hạn tìm kiếm và hiển thị, không thay thế phép xác minh hình học.

## 2. Bản đồ thư mục

| Đường dẫn | Trách nhiệm |
|---|---|
| `src/Nesting2D.Core/` | Miền hình học, đọc DXF, thuật toán nesting, validator và xuất DXF. Không phụ thuộc WPF. |
| `src/Nesting2D.UI.Core/` | ViewModel/MVVM, quy tắc ứng dụng, quản lý Part, chạy nesting nền, preview scene, cấu hình, lưu/mở dự án, điều phối xuất. Nhắm `net8.0`, có thể kiểm thử ngoài Windows. |
| `src/Nesting2D.Desktop/` | Ứng dụng WPF Windows: XAML, cửa sổ, canvas/vẽ, tương tác chuột/bàn phím, hộp thoại, PDF. Nên giữ lớp này chủ yếu là UI và adapter. |
| `tests/Nesting2D.Tests/` | Console test harness không cần framework/NuGet bổ sung, kiểm tra lõi DXF/hình học/nesting/xuất. |
| `tests/Nesting2D.UI.Tests/` | Console test harness cho UI Core và một số kiểm tra tĩnh XAML/luồng UI. Không phải kiểm thử tương tác WPF thực. |
| `Nesting2D.sln` | Solution Visual Studio. |

### Các tệp Core quan trọng

- `DxfReader.cs`: đọc DXF ASCII, entities/layers/đơn vị/block; lọc layer, suy ra vùng/kích thước, chuẩn hóa tọa độ thành mm và tạo `DxfPart`.
- `Geometry.cs`: kiểu hình cơ sở và phép biến đổi affine.
- `DxfShape.cs`: hình học chi tiết theo các segment `LINE`/`ARC`/`CIRCLE`; đưa hình vào vị trí và hướng đặt.
- `ShapeGeometry.cs`: nối segment thành loop/contour, xác định biên ngoài/lỗ, diện tích, phép thử khoảng cách/chứa nhau và xoay.
- `NestEngine.cs`: kiểm tra contour trước khi giao việc cho thuật toán raster.
- `RasterNester.cs`: xếp theo mặt nạ bit bảo thủ, nhiều thứ tự thử và thời hạn/điều kiện dừng.
- `Validator.cs`: kiểm tra kết quả sau nesting hoặc vị trí kéo/xoay thủ công, độc lập với bộ tìm ứng viên.
- `Export.cs`, `DxfExportWriter.cs`, `DxfExportOptions.cs`, `MicroJointProcessor.cs`: API/định dạng xuất DXF; micro-joints được tạo bằng cách bỏ các đoạn ngắn trên biên ngoài, không thay đổi contour lỗ.

### Các tệp UI Core quan trọng

- `MainViewModel.cs`: trạng thái chính của ứng dụng, commands, danh sách Part, preflight, chạy theo nhóm vật liệu/độ dày, kết quả, thao tác thủ công và trạng thái stale.
- `PartItem.cs`: biểu diễn chi tiết trong UI, gồm nguồn DXF hoặc hình chữ nhật, số lượng, layer được bật, metadata vật liệu/độ dày và tính hợp lệ.
- `NestSettings.cs`: thiết lập, kiểm tra đầu vào, chuyển sang `SheetDefinition`/`NestOptions`.
- `NestingService.cs`: chuyển tác vụ Core sang `Task.Run`, sau đó chạy `Validator.Check`.
- `ProjectStore.cs`: serialize dự án `.nest2d` JSON; đường dẫn DXF ghi tương đối với dự án.
- `Preview.cs`: dựng scene xem trước và hit-test hình dạng.
- `ExportService.cs`: điều phối xuất một file hoặc nhiều file và tạo nội dung tóm tắt.

### Các tệp Desktop quan trọng

- `App.xaml`/`App.xaml.cs`: resources, theme và khởi tạo ứng dụng.
- `MainWindow.xaml`/`MainWindow.xaml.cs`: giao diện chính, bind tới ViewModel, nhận kéo-thả, menu/hộp thoại và lifecycle cửa sổ.
- `SheetView.cs`: vẽ tấm, zoom/pan, hit-test, kéo chi tiết đã đặt và phím tắt; placement mới từ Bộ lọc chi tiết được quy đổi về tọa độ tấm trong `MainWindow`, còn quyết định cuối cùng về tính hợp lệ thuộc Core.
- `SheetThumbnailView.cs`, `PartPreviewView.cs`: biểu diễn preview phụ.
- `PdfReportWriter.cs`, `ReportPreviewWindow.cs`, `NestingReport.cs`: dựng và xem trước báo cáo PDF.
- `DxfExportDialog.cs`, `PdfExportOptionsWindow.cs`: hộp thoại cấu hình xuất; các thiết lập nesting chính được bind trực tiếp trên tab **Thiết lập** trong `MainWindow.xaml`.

## 3. Luồng khởi động và phụ thuộc

```text
Nesting2D.Desktop (WPF) ──> Nesting2D.UI.Core ──> Nesting2D.Core
Nesting2D.Tests ─────────────────────────────> Nesting2D.Core
Nesting2D.UI.Tests ──────────────────────────> Nesting2D.UI.Core ──> Nesting2D.Core
```

Desktop tạo `MainViewModel` cùng dịch vụ hộp thoại WPF (`WpfDialogService`) và gắn binding/view. UI Core tránh phụ thuộc WPF để logic có thể chạy và test trên hệ điều hành khác.

## 4. Luồng người dùng: từ DXF đến kết quả

### 4.1 Thêm chi tiết

Người dùng chọn file/thư mục hoặc kéo-thả vào cửa sổ. `MainWindow` chuyển đường dẫn cho `MainViewModel.AddPaths(...)`. ViewModel liệt kê DXF (thư mục hiện chỉ quét cấp trên), bỏ qua file trùng theo đường dẫn đầy đủ, rồi gọi `PartItem.FromDxf`/`DxfReader.Read`.

Trong `DxfReader`, chuỗi xử lý chính:

1. `ReadRaw` nạp DXF ASCII, phân tích header, blocks và entity; chuẩn hóa INSERT/transform, ghi warning cho entity chưa hỗ trợ.
2. Chọn layer: nếu người dùng chỉ định `IncludeLayers` thì dùng đúng layer đó; nếu không, loại layer có từ khóa mặc định như BEND/DIM/TEXT.
3. Chọn layer xác định kích thước. Nếu có `OUTER_PROFILE`, layer đó quyết định hộp bao; nếu không, dùng các layer đầu ra.
4. Hình học nằm ngoài vùng chi tiết được loại khỏi output và cảnh báo.
5. Lấy tỉ lệ đơn vị từ `$INSUNITS`, option cưỡng bức, hoặc giả định mm có warning.
6. Biến đổi tất cả hình hợp lệ sang mm, đưa góc hộp bao về (0,0), chuyển thành các segment của `DxfShape`.
7. Phân tích quy ước tên file `_xN` (số lượng) và `_norot` (cấm xoay).

`PartItem` giữ nguồn file/layer để có thể nạp lại hình khi người dùng bật/tắt layer. Kiểm tra dữ liệu đầu vào xem cả geometry, kích thước và số lượng.

### 4.2 Thiết lập và preflight

Người dùng cấu hình tự động xoay, hướng ưu tiên lấp, bước xoay, thời gian tối đa, hiệu suất mục tiêu và lề/khoảng cách trên tab **Thiết lập**; presets vật liệu/độ dày và kích thước tấm được quản lý tại trang **Sheets**. Trên thanh điều hướng bên trái, tab Thiết lập là mục độc lập sau Export. Khi chạy, `MainViewModel.RunAsync`:

1. Chặn nếu đang chạy; gọi `NestSettings.Validate`.
2. Chia các Part hợp lệ và bỏ qua Part không hợp lệ/số lượng không dương.
3. Kiểm tra metadata vật liệu/độ dày, preset khổ phù hợp và contour DXF (`ShapeOps.Build`). Contour không đáng tin cậy sẽ dừng trước khi nest.
4. Làm tên chi tiết duy nhất vì tên là khóa dùng để ghép placement về số lượng còn lại.
5. Khởi tạo cancellation token, trạng thái tiến độ và đồng hồ tổng.

### 4.3 Nhóm vật liệu và khổ tấm

Các Part được gom theo khóa vật liệu (trim + uppercase) và độ dày làm tròn 4 chữ số thập phân. Mỗi nhóm chỉ xét preset khổ tấm có cùng vật liệu/độ dày. Mỗi vòng lặp:

1. Tạo `PartDefinition` với quantity còn lại.
2. Thử các preset khổ thích hợp, phân bổ phần thời gian còn lại giữa các lượt thử.
3. Chọn phương án theo số chi tiết đặt trên tấm đầu tiên, sau đó utilization và diện tích khổ.
4. Nếu chỉ có một khổ, áp dụng mọi sheet trong kết quả trial; nếu nhiều khổ, lấy sheet đầu tiên của phương án tốt nhất rồi lặp lại lựa chọn.
5. Gán lại instance number toàn cục trong nhóm, giảm số lượng còn lại, ghi chú sheet.
6. Dừng theo hoàn tất, hết thời gian hoặc cancellation; ghi rõ phần chưa xếp.

Lưu ý: dữ liệu dự án hiện lưu danh sách preset nhưng không có tồn kho vật liệu; số sheet được tạo theo lượng cần dùng. Quy tắc so khớp preset và cách chọn khổ là điểm cần giữ nhất quán khi sửa UI Core.

### 4.4 Thuật toán nesting lõi

`NestEngine.Run` chặn các contour không tin cậy rồi gọi `RasterNester.Run`.

`RasterNester` tạo lưới theo kích thước tấm và các variant của mỗi loại chi tiết theo góc xoay. Raster được xây bảo thủ: ô chạm vật liệu được đánh dấu chiếm, nhưng vùng lỗ được giữ trống. Các part đặt trước được giãn theo khe hở. Thuật toán dùng phép toán bit để tìm vị trí ứng viên nhanh và xác minh ứng viên bằng hình học chính xác (`ShapeOps.Conflict`). Nó thử các thứ tự sắp xếp theo diện tích, cạnh dài, cạnh ngắn, chu vi, có thêm số lượt xáo trộn tất định. Có thể dừng sớm khi đạt utilization mục tiêu. `MaxSeconds` bị giới hạn trong khoảng nội bộ 0.05–3600 giây.

`NestResult` mang sheet, placement, utilization, ghi chú, chiến lược và danh sách chưa đặt. Utilization dùng diện tích vật liệu thật (không tính lỗ), theo mô tả trong README. Khi có lỗ/contour lõm, hộp bao không thể dùng để kết luận va chạm.

### 4.5 Kiểm tra kết quả và hiển thị

Sau mỗi run trong UI, `NestingService.RunAsync` chạy `NestEngine.Run` trên thread nền và thực hiện `Validator.Check`. Kết quả được đưa vào ViewModel, sau đó ViewModel cập nhật các `SheetItem`, sheet đang chọn, summary và preview `Scene`. Khi dữ liệu đầu vào thay đổi, kết quả đánh dấu `IsStale`; người dùng cần chạy lại trước khi coi đó là kết quả đồng bộ.

Preview dùng hit-test hình thật để không chọn vùng rỗng trong lỗ. Kéo part từ Bộ lọc chi tiết gọi `TryPlaceManually`, tạo kết quả thủ công khi chưa có nest và đặt tâm part tại điểm thả; `Validator.CheckPlacement` kiểm tra lề, spacing và contour. `PartItem.Quantity` là số nguồn; `RemainingQuantity` được tính từ tổng placement theo tên trên tất cả sheets và cập nhật ngay sau khi đặt/xóa. Bộ lọc hiển thị số còn lại. Các thao tác thêm tấm thủ công, xóa placement được chọn và xóa toàn bộ nesting nằm trong context menu chuột phải của danh sách **Kết quả Nesting**. Nhập Part/Sheet và xóa Part nguồn không còn nút chung trên thanh công cụ: chúng thuộc trang Part/Sheets và menu ngữ cảnh Part tương ứng.

Canvas hỗ trợ chọn đơn, Ctrl-click để cộng/trừ selection và quét vùng trống để chọn nhiều placement. Kéo hoặc dùng mũi tên tác động cả nhóm. ViewModel tạo tập vật cản không chứa nhóm đang di chuyển, kiểm tra lần lượt các ứng viên với vật cản và thành viên nhóm đã xác nhận, rồi mới áp dụng toàn bộ; thao tác bị từ chối nếu bất kỳ thành viên nào không hợp lệ. Mũi tên dò bước 1 mm rồi tinh chỉnh bằng chia đôi; `Shift` + mũi tên dịch đúng 1 mm. Kiểm tra kéo được giới hạn tần suất để giảm tải UI; khi thả vẫn xác minh đồng bộ lại toàn nhóm trước khi ghi. Chuột phải trong danh sách **Kết quả Nesting** để thêm sheet hoặc xóa placement được chọn/toàn bộ kết quả; `Delete` cũng xóa placement đang chọn, không xóa Part nguồn. `R`/nhấp đúp xoay 90°, `M` phản chiếu contour trái-phải. Chỉnh tay không tự nest lại các part còn lại; trạng thái edited/stale phải được giữ rõ.

### 4.6 Lưu và mở dự án

`ProjectStore.Save` lưu settings dưới dạng JSON trong trường `Settings`, cùng danh sách Part. Hình chữ nhật lưu width/height; chi tiết DXF lưu đường dẫn tương đối với file `.nest2d`, danh sách layer, tên, số lượng, trạng thái xoay, vật liệu, độ dày và flip.

`ProjectStore.Load` phục hồi settings, tạo Part chữ nhật trực tiếp, hoặc nạp lại DXF từ đường dẫn tương đối. Thiếu/lỗi một DXF được ghi vào `problems` và bỏ qua item đó thay vì làm hỏng toàn bộ phiên nạp. Vì DXF ngoài vẫn là nguồn geometry, khi di chuyển dự án sang máy/thư mục khác cần giữ các file DXF tại vị trí tương đối tương ứng.

### 4.7 Xuất DXF/PDF

Lệnh export của UI gọi `ExportService`, sau đó `DxfWriter`/`DxfExportWriter` ghi DXF. Tùy chọn có thể quyết định một file hay mỗi sheet một file, layer, nhãn, block, đơn vị, hướng nest và kiểu geometry. Tùy chọn micro-joints (mặc định tắt) ngắt đường cắt trên biên ngoài; độ rộng nhập theo mm và số lượng là mục tiêu mỗi part. Bộ xử lý giữ nguyên lỗ/contour trong, và giữ nguyên hình học gốc ngoài các khoảng ngắt. Đây là đường cắt bị ngắt chứ không phải đường bù CAM; cần xác minh DXF sau export trên CAM/máy cắt đích. PDF được tạo bởi lớp Desktop dùng PDFsharp; đây là phần Windows UI/report, không phải dịch vụ Core.

Các kiểm thử lõi có đường đi xuất rồi đọc lại DXF để xác nhận segment/placement. Tuy vậy, kiểm thử round-trip parser không chứng minh file mở hoàn hảo trong AutoCAD, ZWCAD hay Inventor; cần kiểm tra compatibility trên các ứng dụng CAD mục tiêu trước khi phát hành sản xuất.

## 5. Kiểu dữ liệu và bất biến cần giữ

- `PartDefinition`: dữ liệu đầu vào ổn định cho Core: tên, kích thước, quantity, cho phép xoay và geometry tùy chọn.
- `DxfShape`: geometry chuẩn hóa, đơn vị mm, tọa độ gốc nằm tại góc dưới-trái hộp bao.
- `SheetDefinition`: kích thước tấm, margin và spacing; không gộp spacing với margin.
- `Placement`: instance của part trên sheet, tọa độ, bounding dimensions, thông tin xoay/geometry.
- `NestResult`/`SheetResult`: kết quả nest và trạng thái chưa đặt.
- `NestSettings`: thiết lập người dùng; `ToSheet`/`ToOptions` là ranh giới chuyển sang Core.
- `PartItem`: trạng thái UI nhiều hơn model lõi, không truyền trực tiếp vào thuật toán.

Bất biến: đơn vị hình học vào Core là mm; placement phải nằm trong lề; spacing giữa các hình phải đạt yêu cầu trong dung sai đã công bố; DXF lỗi không được fallback thành hộp bao; quantity không bị nhân đôi khi lặp nhiều sheet; mỗi output có index/instance ổn định trong phạm vi run.

## 6. Cách chạy và kiểm tra

Cần .NET 8 SDK. Trên Windows có thể mở `Nesting2D.sln`, hoặc chạy ứng dụng desktop:

```powershell
dotnet run --project src/Nesting2D.Desktop -c Release
```

Build solution và chạy console harness khi task cho phép:

```powershell
dotnet build Nesting2D.sln -c Release
dotnet run --project tests/Nesting2D.Tests -c Release
dotnet run --project tests/Nesting2D.UI.Tests -c Release
```

Desktop chỉ chạy trên Windows. Có thể cross-target build từ Linux/macOS nếu SDK được cấu hình Windows targeting; build cross-target không thay thế kiểm thử UI trên Windows.

## 7. Chất lượng mã: kết quả rà soát và giới hạn xác minh

### Điểm tốt

- Tách Core khỏi WPF; cấu trúc hỗ trợ test logic đa nền tảng.
- Nullability và implicit usings được bật trong các project chính.
- Có hai bộ test tự chạy, bao phủ nhiều nhánh quan trọng: DXF units/layers/bulge/ellipse/spline/INSERT, hình học xoay, nesting, validator, save/load UI, cancellation, stale state và thao tác thủ công.
- Có validator độc lập sau nesting; có preflight contour; các lỗi nhập DXF được đưa thành warning/thông báo.
- `.gitignore` đã bỏ qua `bin/`, `obj/`, `.vs/`, `out/` và artefact IDE.

### Các điểm cần cải thiện lâu dài

1. **Chuyển test harness sang test framework chuẩn.** Hai project test hiện là console executable; `dotnet test` không tự phát hiện assertion như xUnit/NUnit. Dùng xUnit/NUnit + `Microsoft.NET.Test.Sdk` để IDE/CI có test discovery, report và lọc test. Có thể giữ console runner tạm thời trong khi chuyển dần.
2. **Thêm CI đa nền tảng.** Build/test Core và UI Core trên Windows/Linux; build Desktop trên Windows. Lưu artifact/log và kiểm tra format/lint để phát hiện lỗi sớm.
3. **Đưa cảnh báo compiler và phân tích tĩnh thành cổng chất lượng.** Bật warnings-as-errors có chọn lọc, `dotnet format --verify-no-changes`, analyzer; giải quyết cảnh báo theo nhóm thay vì bật cứng toàn bộ ngay nếu code hiện có chưa sạch.
4. **Quản lý version dự án `.nest2d`.** DTO có `Version` nhưng loader chưa thể hiện migration theo version. Cần từ chối version tương lai không hỗ trợ, validate schema, migration có test và lưu atomic (ghi file tạm rồi rename) để giảm nguy cơ hỏng file khi crash.
5. **Quản lý tham chiếu DXF bền vững.** Đường dẫn tương đối tiện cho di chuyển nhưng file nguồn có thể bị mất/thay đổi. Cân nhắc checksum/metadata thời gian sửa, cảnh báo source changed, tùy chọn copy/embed file nguồn hoặc đóng gói dự án.
7. **Làm rõ tính tất định và song song của heuristic.** `RasterNester` dùng `Parallel.ForEach` cho các thứ tự thử, trong khi giới hạn thời gian và early-stop chung có thể khiến phương án cuối phụ thuộc lịch chạy. Nếu cần tái lập kết quả, ghi seed/cấu hình/phiên bản thuật toán, cân nhắc chạy tuần tự hoặc gom kết quả theo cơ chế determinism rõ ràng; thêm benchmark.
8. **Thiết lập timeout và cancellation có test biên.** Thời gian được tính trong lõi và lại được phân bổ cho các trial theo nhóm/khổ. Tiếp tục test lượng dữ liệu lớn, cancellation khi đang tạo variant, nhiều vật liệu và trường hợp 0/âm/NaN/infinity; bảo đảm mọi vòng lặp nặng có điểm kiểm tra cancellation.
9. **Kiểm thử DXF interoperability.** Thêm bộ fixture do CAD thực xuất, kiểm tra phiên bản DXF, OCS/extrusion, bulge, SPLINE, blocks/arrays, encoding, layer và round-trip; xác nhận thủ công trên ZWCAD/Inventor/AutoCAD phù hợp phiên bản đích.
10. **Hiệu năng/bộ nhớ raster.** Đặt giới hạn kích thước lưới/đầu vào, đo benchmark theo sheet/chi tiết/độ phân giải; xử lý file DXF lớn hoặc số lượng cực lớn có tiến độ và giới hạn tài nguyên. Không để input không tin cậy tạo cấp phát bất thường.
11. **Chuẩn hóa ranh giới UI và service.** `MainViewModel` đang điều phối khá nhiều bước nghiệp vụ (preflight, grouping, chọn khổ, ghép quantity, stale/preview). Khi tính năng tăng, tách use-case/service cụ thể và immutable request/result để dễ test mà không phá kiến trúc hiện tại.
12. **Phân loại lỗi thay vì catch tổng quát.** Những ranh giới UI/nhập file có thể bắt lỗi để tiếp tục batch, nhưng nên phân biệt lỗi định dạng/IO/operation cancelled/lỗi lập trình, tránh che lỗi bất ngờ bằng thông báo chung.
13. **Tài liệu vận hành và phát hành.** Bổ sung release notes, phiên bản ứng dụng, ma trận Windows/.NET/CAD hỗ trợ, quy trình publish/signing và lưu kết quả kiểm tra CAD.

### Kiểm tra đã thực hiện cho bản được gửi

- Đã kiểm kê mã nguồn, solution, README, project files, thuật toán Core, luồng ViewModel/service/persistence và một phần nội dung tests.
- ZIP ban đầu chứa artefact Visual Studio và build: khoảng 5.6 MiB tổng; trong đó `bin` khoảng 3.31 MiB, `obj` 1.06 MiB, `.vs` 0.68 MiB. Chúng không phải source và đã được loại khỏi bản làm việc/tài liệu giao.
- Trong phiên cập nhật thao tác thủ công, đã chạy `dotnet run --project tests/Nesting2D.Tests/Nesting2D.Tests.csproj`: 35 đạt, 0 lỗi; và `dotnet run --project tests/Nesting2D.UI.Tests/Nesting2D.UI.Tests.csproj`: 36 đạt, 0 lỗi (bao gồm regression số lượng còn lại, thêm sheet, di chuyển/xóa selection và xóa toàn bộ nest). Lượt gọi đầu dùng `--no-restore` thất bại vì không có assets; sau đó chạy lại có NuGet restore và thành công.
- UI test harness có kiểm tra tĩnh XAML/binding/handler và cho biết không thể biên dịch WPF trên Linux. Vì vậy phần code-behind WPF và tương tác thực tế chưa được xác minh bằng build/chạy Windows.
- Chưa xác nhận giao diện tương tác trên Windows thật và chưa mở DXF xuất trong CAD mục tiêu.

## 8. Quy tắc cho AI/nhà phát triển tiếp theo

Quy tắc chuẩn và có tính ràng buộc nằm trong [`DEVELOPMENT_RULES.md`](DEVELOPMENT_RULES.md).

1. Đọc `README.md` và tài liệu này trước khi sửa.
2. Trước khi thay đổi thuật toán, kiểm tra các test hình học và hiểu hệ trục, đơn vị, `Place`, `Rotated`, margin/spacing. Thêm test tái hiện bug trước khi sửa.
3. Không đổi sang bounding-box fallback cho chi tiết contour lỗi; đây là quy tắc an toàn cốt lõi.
4. Core không tham chiếu WPF; UI Core không tham chiếu Desktop. Giữ phụ thuộc một chiều như sơ đồ.
5. Khi đổi format `.nest2d`, tăng/migrate version và giữ tương thích dự án cũ.
6. Khi đổi output DXF, thêm test round-trip và xác minh CAD thực; đừng xem parser nội bộ là bằng chứng interoperability đầy đủ.
7. Sau mọi thay đổi, chạy build và các test runner theo yêu cầu của task; nếu người dùng yêu cầu chưa chạy test thì tuyệt đối không chạy. Build Desktop trên Windows.
8. Phân biệt kết quả cũ (`IsStale`) và kết quả đã chỉnh tay (`IsEdited`) trong mọi hành vi lưu/xuất.
9. Không xóa file người dùng hay artefact cần thiết một cách mù quáng. `bin/obj/.vs` có thể tái tạo; DXF nguồn và `.nest2d` thì không.

## 9. Sơ đồ vòng đời rút gọn

```text
DXF / Part hình chữ nhật
        │
        └── Desktop: AddPaths → PartItem → DxfReader
                               │
                    geometry chuẩn hóa mm
                               │
     UI preflight + nhóm vật liệu/độ dày + thiết lập ứng dụng
                               │
                 NestEngine → RasterNester
                               │
                NestResult + danh sách chưa đặt
                               │
              Validator độc lập / cập nhật preview
                    │                    │
              kéo-xoay hợp lệ       export DXF / PDF
                    │                    │
          đánh dấu edited/stale     lưu file kết quả
```
