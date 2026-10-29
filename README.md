# Nesting2D — nesting 2D cho tấm kim loại

Ứng dụng desktop Windows dùng WPF để đọc file DXF ASCII của từng chi tiết (ví dụ xuất từ Inventor flat pattern hoặc ZWCAD), nesting trên tấm, xem/chỉnh vị trí và xuất DXF/PDF. Dự án chỉ phát triển ứng dụng desktop; không còn thành phần dòng lệnh.

- [Quy tắc phát triển dành cho AI và nhà phát triển](DEVELOPMENT_RULES.md)
- [Hướng dẫn kiến trúc và luồng hoạt động](PROJECT_GUIDE.md)

## Cấu trúc

```text
src/Nesting2D.Core       Hình học, DXF, nesting, validator, xuất DXF (net8.0)
src/Nesting2D.UI.Core    ViewModel, nghiệp vụ UI, lưu dự án và điều phối (net8.0)
src/Nesting2D.Desktop    Giao diện WPF và báo cáo PDF (net8.0-windows)
tests/Nesting2D.Tests    Console harness kiểm tra Core
 tests/Nesting2D.UI.Tests Console harness kiểm tra UI Core
```

## Chạy ứng dụng (Windows)

Cần .NET 8 SDK hoặc Visual Studio 2022. Mở `Nesting2D.sln`, đặt `Nesting2D.Desktop` làm Startup Project và chạy; hoặc:

```powershell
dotnet run --project src/Nesting2D.Desktop -c Release
```

Publish framework-dependent cho Windows x64:

```powershell
dotnet publish src/Nesting2D.Desktop -c Release -r win-x64 --self-contained false
```

Kéo file/thư mục DXF vào cửa sổ (hoặc chọn **Thêm DXF** trong trang Part), chỉnh số lượng và gán vật liệu/khổ tấm. Bấm **Nesting (F5)** để xếp tự động, hoặc kéo một chi tiết từ **Bộ lọc chi tiết** vào vùng tấm để đặt thủ công; số lượng trong bộ lọc thể hiện số còn lại và vị trí đặt vẫn được kiểm tra theo lề, khoảng cách và contour thật. Nhấp chuột phải trong danh sách **Kết quả Nesting** để thêm tấm thủ công, xóa placement đang chọn hoặc xóa toàn bộ bố trí (danh sách Part được giữ lại). Trên hình tấm: lăn chuột để zoom; kéo nền/chuột phải để pan; kéo part đã đặt để di chuyển; Ctrl-click hoặc quét vùng để chọn nhiều part; `Delete` xóa placement đang chọn. Nhấp đúp hoặc `R` xoay 90°; `M` lật; phím mũi tên đẩy nhóm được chọn tới vật cản/mép tấm, còn `Shift` + mũi tên dịch đúng 1 mm mỗi lần. **Thiết lập** ở thanh điều hướng bên trái chứa tùy chọn tự động xoay, hướng lấp, bước xoay, thời gian, mục tiêu hiệu suất, lề và khoảng cách. Khi xuất DXF, có thể bật micro-joints và chỉ định độ rộng/số cầu; các cầu này ngắt đường cắt ở biên ngoài và cần được kiểm tra trong phần mềm CAM trước khi sản xuất. Các nút Nhập Part/Nhập Sheet/Xóa trên thanh công cụ chính đã được bỏ; thao tác nhập/xóa nằm tại trang Part/Sheets. Dự án lưu thành `.nest2d`.

## Build và kiểm tra

```powershell
dotnet build Nesting2D.sln -c Release
dotnet run --project tests/Nesting2D.Tests -c Release
dotnet run --project tests/Nesting2D.UI.Tests -c Release
```

Chưa chạy test? Hãy tuân theo chỉ dẫn trong yêu cầu công việc; các harness hiện là chương trình console. Desktop chỉ chạy trên Windows. Có thể cross-target build trên hệ điều hành khác nếu SDK được cấu hình Windows targeting, nhưng cần kiểm tra giao diện tương tác trên Windows thật.

## Chuẩn bị DXF

1. Mỗi chi tiết một file `.dxf` ASCII (không hỗ trợ binary DXF).
2. Đặt số lượng trong tên file: `Gia_do_A_x12.dxf` = 12 chi tiết. Không có `_xN` thì mặc định 1.
3. Cấm xoay chi tiết có vân thớ bằng hậu tố `_norot`, ví dụ `Tam_op_x5_norot.dxf`.

### Layer và đơn vị

- Nếu có layer chứa `OUTER_PROFILE` (Inventor: `IV_OUTER_PROFILE`) thì layer này quyết định kích thước.
- Các layer có tên chứa `BEND`, `CENTER`, `TANGENT`, `DIM`, `TEXT`, `NOTE`, `DEFPOINTS`, `HIDDEN`, `CONSTRUCTION`, `UNCONSUMED` mặc định bị loại. Trong giao diện có thể chọn layer phù hợp cho chi tiết.
- Hình ngoài biên chi tiết ở layer khác có thể bị loại khỏi output kèm cảnh báo.
- Đơn vị lấy từ `$INSUNITS`; nếu không khai báo, chương trình giả định mm và cảnh báo.

## Nesting và xuất

Thuật toán nesting sử dụng contour thật, hỗ trợ lỗ và hình lõm; không fallback sang hộp bao khi contour lỗi. Có hai hướng ưu tiên lấp, xoay theo bước cấu hình, khoảng cách giữa chi tiết, lề tấm, giới hạn thời gian và mục tiêu hiệu suất. Mọi ứng viên raster đều được xác minh lại bằng hình học; kết quả được Validator kiểm tra độc lập.

DXF xuất theo mm, có thể bao gồm đường cắt, biên tấm và nhãn tùy chọn. Layer gốc không được giữ lại; đường cắt xuất trên layer `CUT`. Chỉnh tay chỉ xác minh va chạm/lề, không tự nesting lại các chi tiết còn lại. Cần mở file xuất trên các phần mềm CAD đích để kiểm tra interoperability trước khi dùng sản xuất.

## Giới hạn đã biết

- Raster có dung sai hình học liên quan xấp xỉ cung; xem `PROJECT_GUIDE.md` để biết chi tiết và rủi ro.
- Chưa xác nhận trải nghiệm WPF trên mọi cấu hình Windows và chưa xác minh DXF bằng ứng dụng CAD mục tiêu.
- Bộ kiểm thử hiện là console harness, chưa tích hợp test discovery/CI chuẩn.
