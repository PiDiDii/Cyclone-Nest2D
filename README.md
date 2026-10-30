# Cyclone Nest2D — nesting 2D cho tấm kim loại

Cyclone Nest2D là ứng dụng desktop Windows dùng WPF để đọc file DXF ASCII của từng chi tiết (ví dụ xuất từ Inventor flat pattern hoặc ZWCAD), nesting trên tấm, xem/chỉnh vị trí và xuất DXF/PDF. Dự án chỉ phát triển ứng dụng desktop; không còn thành phần dòng lệnh.

- [Quy tắc phát triển dành cho AI và nhà phát triển](DEVELOPMENT_RULES.md)
- [Hướng dẫn kiến trúc và luồng hoạt động](PROJECT_GUIDE.md)

## Cấu trúc

```text
src/CycloneNest2D.Core       Hình học, DXF, nesting, validator, xuất DXF (net8.0)
src/CycloneNest2D.UI.Core    ViewModel, nghiệp vụ UI, lưu dự án và điều phối (net8.0)
src/CycloneNest2D.Desktop    Giao diện WPF và báo cáo PDF (net8.0-windows)
tests/CycloneNest2D.Tests    XUnit tests cho Core
tests/CycloneNest2D.UI.Tests XUnit tests cho UI Core và kiểm tra tĩnh XAML
tests/CycloneNest2D.Performance Console benchmark nesting (JSON/CSV)
```

## Chạy ứng dụng (Windows)

Cần .NET 8 SDK hoặc Visual Studio 2022. Mở `CycloneNest2D.sln`, đặt `CycloneNest2D.Desktop` làm Startup Project và chạy; hoặc:

```powershell
dotnet run --project src/CycloneNest2D.Desktop -c Release
```

Publish framework-dependent cho Windows x64:

```powershell
dotnet publish src/CycloneNest2D.Desktop -c Release -r win-x64 --self-contained false
```

Kéo file/thư mục DXF vào cửa sổ (hoặc chọn **Thêm DXF** trong trang Part), chỉnh số lượng và gán vật liệu/khổ tấm. Bấm **Nesting (F5)** để xếp tự động, hoặc kéo một chi tiết từ **Bộ lọc chi tiết** vào vùng tấm để đặt thủ công; số lượng trong bộ lọc thể hiện số còn lại và vị trí đặt vẫn được kiểm tra theo lề, khoảng cách và contour thật. Nhấp chuột phải trong danh sách **Kết quả Nesting** để thêm tấm hoặc xóa nesting của tấm đang chọn; nút **Xóa chọn** trên thanh công cụ canvas và phím `Delete` xóa placement đang chọn. Trên hình tấm: lăn chuột để zoom; kéo nền/chuột phải để pan; kéo part đã đặt để di chuyển; Ctrl-click hoặc quét vùng để chọn nhiều part. Nhấp đúp hoặc `R` xoay 90°; `M` lật; phím mũi tên đẩy nhóm được chọn tới vật cản/mép tấm, còn `Shift` + mũi tên dịch đúng 1 mm mỗi lần. **Thiết lập** ở thanh điều hướng bên trái chứa tùy chọn tự động xoay, hướng lấp, bước xoay, thời gian, mục tiêu hiệu suất, lề và khoảng cách. Khi xuất DXF, có thể bật micro-joints và chỉ định độ rộng/số cầu; các cầu này ngắt đường cắt ở biên ngoài và cần được kiểm tra trong phần mềm CAM trước khi sản xuất. Dự án lưu thành `.nest2d` (chỉ mục nhóm vật liệu) cùng các workspace con trong thư mục `.data`; mỗi workspace lưu thiết lập, danh sách chi tiết, tham chiếu DXF và kết quả nesting.

## Build và kiểm tra

```powershell
dotnet build CycloneNest2D.sln -c Release
dotnet test CycloneNest2D.sln -c Release
```

Chạy riêng từng bộ test bằng `dotnet test tests\CycloneNest2D.Tests\CycloneNest2D.Tests.csproj -c Release` hoặc `dotnet test tests\CycloneNest2D.UI.Tests\CycloneNest2D.UI.Tests.csproj -c Release`. Performance vẫn là console riêng: `dotnet run --project tests\CycloneNest2D.Performance -c Release -- --quick --output artifacts\performance\quick`. GitHub Actions kiểm tra Core/UI Core trên Ubuntu và Windows, build Desktop trên Windows, đồng thời lưu báo cáo Performance nhanh làm artifact. Desktop chỉ chạy trên Windows. Có thể cross-target build trên hệ điều hành khác nếu SDK được cấu hình Windows targeting, nhưng cần kiểm tra giao diện tương tác trên Windows thật.

## Chuẩn bị DXF

1. Mỗi chi tiết một file `.dxf` ASCII (không hỗ trợ binary DXF).
2. Đặt số lượng trong tên file: `Gia_do_A_x12.dxf` = 12 chi tiết. Không có `_xN` thì mặc định 1.
3. Cấm xoay chi tiết có vân thớ bằng hậu tố `_norot`, ví dụ `Tam_op_x5_norot.dxf`.

### Layer và đơn vị

- Nếu có layer chứa `OUTER_PROFILE` (Inventor: `IV_OUTER_PROFILE`) thì layer này quyết định kích thước.
- Các layer có tên chứa `BEND`, `CENTER`, `TANGENT`, `DIM`, `TEXT`, `NOTE`, `DEFPOINTS`, `HIDDEN`, `CONSTRUCTION`, `UNCONSUMED` mặc định bị loại. Trong giao diện có thể chọn layer phù hợp cho chi tiết.
- Hình ngoài biên chi tiết ở layer khác có thể bị loại khỏi output kèm cảnh báo.
- `TEXT`/`MTEXT` được giữ như dấu ghi chú, không tham gia tính contour hay đường cắt; vị trí chèn nằm trong contour kín bên trong sẽ tạo cảnh báo khi nhập. Khi xuất R12, `MTEXT` được chuyển thành các dòng `TEXT` để giữ nội dung; phiên bản DXF mới hơn giữ entity `MTEXT`.
- Đơn vị lấy từ `$INSUNITS`; nếu không khai báo, chương trình giả định mm và cảnh báo.

## Nesting và xuất

Thuật toán nesting sử dụng contour thật, hỗ trợ lỗ và hình lõm; không fallback sang hộp bao khi contour lỗi. Có hai hướng ưu tiên lấp, xoay theo bước cấu hình, khoảng cách giữa chi tiết, lề tấm, giới hạn thời gian và mục tiêu hiệu suất. Mọi ứng viên raster đều được xác minh lại bằng hình học; kết quả được Validator kiểm tra độc lập. Với contour thật, `RasterNester.GapTol` là 0,1 mm: kiểm tra khoảng cách dùng `max(0, spacing - 0,1 mm)` để bù sai số rời rạc hóa cung; va chạm/chồng lấn vẫn bị từ chối. Cặp chi tiết chữ nhật được Validator kiểm tra theo khoảng cách chính xác, không dùng dung sai này. Để bảo vệ bộ nhớ và độ phản hồi, mỗi lần nesting nhận tối đa 250.000 chi tiết; kích thước raster và khoảng cách không phù hợp giới hạn tài nguyên sẽ bị từ chối kèm thông báo.

DXF xuất theo mm, có thể bao gồm đường cắt, biên tấm và nhãn tùy chọn. Đường cắt và ghi chú TEXT/MTEXT của chi tiết DXF giữ layer nguồn; chi tiết hình chữ nhật tạo trong ứng dụng xuất trên layer `PARTS`. Có thể bật tùy chọn gộp chi tiết vào một layer khi xuất. Chỉnh tay chỉ xác minh va chạm/lề, không tự nesting lại các chi tiết còn lại. Cần mở file xuất trên các phần mềm CAD đích để kiểm tra interoperability trước khi dùng sản xuất.

## Quản lý dự án nhiều vật liệu

Một file `.nest2d` là chỉ mục của dự án; các nhóm vật liệu/độ dày được lưu thành file workspace riêng trong thư mục `<tên dự án>.data` cạnh file chỉ mục. Mỗi nhóm chứa danh sách chi tiết, thiết lập/khổ tấm và kết quả nesting, cho phép chuyển qua lại mà không trộn dữ liệu. Có thể tạo, đổi tên, nhân bản hoặc gỡ nhóm ở trang **Tổng quan**; gỡ nhóm chỉ bỏ liên kết khỏi dự án, không xóa file workspace. File dự án dạng cũ vẫn mở được và được chuyển sang cấu trúc mới khi lưu.

## Giới hạn đã biết

- Dung sai khe hở 0,1 mm cho contour thật có thể khiến khoảng cách nhỏ hơn spacing cấu hình tối đa 0,1 mm; xem `PROJECT_GUIDE.md`.
- Chưa xác nhận trải nghiệm WPF trên mọi cấu hình Windows và chưa xác minh DXF bằng ứng dụng CAD mục tiêu.
