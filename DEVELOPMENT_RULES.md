# Quy tắc phát triển chuẩn — Cyclone Nest2D

**Mục đích:** Đây là tài liệu quy tắc làm việc có tính chuẩn tắc cho nhà phát triển và AI agent. Đọc tài liệu này trước khi lập kế hoạch hoặc sửa mã. Khi mô tả kiến trúc chi tiết, tham chiếu thêm [`PROJECT_GUIDE.md`](PROJECT_GUIDE.md); khi mô tả cách dùng, tham chiếu [`README.md`](README.md).

Các từ **PHẢI**, **KHÔNG ĐƯỢC**, **NÊN** và **CÓ THỂ** mang nghĩa quy phạm tương ứng. Nếu yêu cầu của người dùng xung đột với quy tắc, phải nêu xung đột và hỏi/diễn giải trước khi thực hiện thay đổi rủi ro; không âm thầm bỏ qua bất biến an toàn.

## 1. Phạm vi sản phẩm

1. Cyclone Nest2D là **ứng dụng desktop WPF cho Windows**. Không tạo lại CLI, project dòng lệnh, parser đối số, luồng nhập CSV cho CLI, ví dụ lệnh CLI hoặc tài liệu hướng dẫn CLI.
2. Không thêm lại thư mục/project `src/CycloneNest2D.Cli`, tham chiếu CLI vào `CycloneNest2D.sln`, hoặc sample/data chỉ tồn tại để phục vụ CLI, trừ khi chủ dự án yêu cầu rõ ràng thay đổi quyết định này.
3. Giao diện desktop và các luồng DXF, `.nest2d`, nesting, chỉnh tay, DXF/PDF là các trải nghiệm sản phẩm được duy trì.
4. Không suy diễn rằng “bỏ CLI” đồng nghĩa bỏ Core, DXF reader, export hoặc UI Core. Các thành phần này vẫn cần cho Desktop.

## 2. Ranh giới kiến trúc

Phụ thuộc hợp lệ:

```text
CycloneNest2D.Desktop → CycloneNest2D.UI.Core → CycloneNest2D.Core
tests/CycloneNest2D.UI.Tests → CycloneNest2D.UI.Core → CycloneNest2D.Core
tests/CycloneNest2D.Tests → CycloneNest2D.Core
```

- `CycloneNest2D.Core` là lõi miền độc lập UI: geometry, DXF, nesting, validation, export.
- `CycloneNest2D.UI.Core` nắm use-case, ViewModel, settings, persistence và preview logic; không tham chiếu WPF/Desktop.
- `CycloneNest2D.Desktop` nắm XAML, render, input, dialog, lifecycle và adapter hệ điều hành; không đặt thuật toán miền lớn trong code-behind nếu có thể đặt ở Core/UI Core.
- Không tạo phụ thuộc vòng. Không để Core tham chiếu UI Core hoặc Desktop.
- Khi thay đổi ranh giới, cập nhật sơ đồ trong tài liệu kiến trúc và kiểm tra toàn bộ project references.
- `MainViewModel` chỉ nên điều phối state/command; use-case mới về nesting, placement thủ công, project/workspace hoặc export phải thuộc service/use-case có trách nhiệm riêng, không tiếp tục dồn vào ViewModel hiện tại.
- Code-behind WPF xử lý sự kiện, lifecycle và chuyển đổi input/view; không tự chứa quy tắc miền, persistence hoặc workflow mới nhiều bước.
- File/lớp lớn hiện hữu là nợ kiến trúc, không phải tiền lệ để tiếp tục mở rộng. Với phần việc mới độc lập, tách theo trách nhiệm trước hoặc cùng lúc với thay đổi; refactor theo lát cắt có test, không viết lại toàn bộ ứng dụng một lần.

## 3. Bất biến hình học và an toàn sản xuất

1. Đơn vị nội bộ của hình học là **mm**. Mọi import phải chuẩn hóa đơn vị trước khi đưa vào thuật toán; mọi output phải ghi rõ/quy đổi đơn vị theo tùy chọn.
2. Chi tiết DXF phải được nesting, kiểm tra khoảng cách và xuất dựa trên **contour thật**, bao gồm lỗ và hình lõm khi được hỗ trợ.
3. **KHÔNG ĐƯỢC** dùng hộp bao làm phương án thay thế để chấp nhận contour DXF hở, lỗi hoặc không thể xác minh. Trong trường hợp đó phải dừng/từ chối placement và giải thích nguyên nhân.
4. Hộp bao chỉ phục vụ các mục đích phù hợp như kích thước, broad-phase culling, bounds, UI và kiểm tra rectangle-only; không dùng để kết luận hai hình thật không va chạm.
5. Margin mép tấm và spacing giữa các chi tiết là hai đại lượng riêng. Không gộp, đổi nghĩa hoặc bỏ dung sai của chúng.
6. Thay đổi phép xoay, gốc tọa độ, transform block/OCS, arc/bulge, loop orientation, chứa lỗ hoặc distance test PHẢI có test hồi quy hình học.
7. Mọi placement do thuật toán trả về và thao tác đặt/kéo/xoay/lật tay phải được xác minh độc lập bằng `Validator`/API hợp lệ tương ứng.
8. Dung sai hình học phải được giữ nhất quán giữa raster, exact check và validator. Nếu thay đổi, cập nhật docs, thông báo người dùng nếu ảnh hưởng chất lượng cắt, và thêm test sát biên.
9. `RasterNester.GapTol` hiện là 0,1 mm để bù sai số rời rạc hóa cung. Với contour thật, ngưỡng xác minh khoảng cách là `max(0, spacing - GapTol)`; không chấp nhận chồng lấn và không áp dụng dung sai này cho cặp chữ nhật thuần. Không được thay đổi giá trị/ngữ nghĩa nếu không có benchmark, regression test sát biên và cập nhật tài liệu.
10. Không báo một nest là “đã xếp đủ” nếu còn `Unplaced`, bị hủy hoặc hết thời gian. Trạng thái một phần phải hiển thị rõ.

## 4. Quy tắc nhập DXF và dữ liệu

- Không làm suy giảm xử lý lỗi: file sai định dạng, thiếu layer, đơn vị không rõ, entity không hỗ trợ và contour lỗi cần thông báo phù hợp, không crash ngoài ý muốn.
- Nếu thêm entity DXF hoặc mở rộng xử lý INSERT/block, phải có fixture/test về tọa độ, scale, rotation, layer inheritance và đơn vị liên quan.
- Layer được chọn/bỏ chọn phải làm geometry tính nesting, preview và DXF export nhất quán.
- Micro-joints chỉ được tạo ở bước xuất theo tùy chọn; không sửa DXF nguồn hay placement geometry. Độ rộng tính bằng mm; contour lỗ và biên trong phải được giữ nguyên. Cần regression test cho các đoạn LINE/ARC và ghi rõ cần xác minh CAM/máy cắt đích.
- Thay đổi quy tắc tên file (`_xN`, `_norot`) phải cập nhật hướng dẫn và test tương ứng.
- Không ghi đè file người dùng hoặc thay đổi file DXF nguồn khi chỉ cần nhập để nesting.
- File `.nest2d` là dữ liệu người dùng. Thay đổi schema phải tăng version/migration có chủ đích, giữ khả năng đọc file cũ theo chính sách tương thích đã công bố, và thêm test load/save. Không tùy tiện đổi nghĩa đường dẫn tương đối.
- Khi đọc file dự án thiếu nguồn DXF, tiếp tục xử lý các item còn lại nếu hợp lý nhưng phải đưa lỗi cho người dùng; không âm thầm bỏ dữ liệu.

## 5. Quy tắc luồng UI và trạng thái

1. Tác vụ nesting nặng phải chạy ngoài UI thread; cập nhật tiến độ/state phải an toàn với dispatcher/context của UI.
2. Hỗ trợ cancellation ở các bước nặng; luôn giải phóng token/resource trong `finally` và giữ trạng thái `IsBusy` đúng.
3. `IsStale` có nghĩa dữ liệu đầu vào/settings đã thay đổi so với kết quả. Không xuất/hiển thị kết quả stale như thể mới tính nếu UI chưa cảnh báo.
4. `IsEdited` biểu thị placement đã bị người dùng can thiệp; không được vô tình xóa cờ hoặc ghi đè chỉnh sửa khi đổi selection/refresh preview.
5. Preview, hit-test, kéo, xoay và export cần dùng cùng quy ước tọa độ/placement; lỗ không được coi là phần vật liệu có thể chọn/va chạm.
6. Mọi input người dùng có validation và thông báo dễ hành động; không chỉ ghi exception vào log.
7. Không thực hiện thay đổi UI diện rộng hoặc thao tác file bên ngoài một cách bất ngờ. Không tự xóa `.nest2d`, DXF nguồn, kết quả export hoặc cấu hình người dùng.
8. Nesting thủ công phải giữ `PartItem.Quantity` là số nguồn bất biến; số còn lại là projection theo tổng placement trên tất cả sheet, không sửa quantity gốc.
9. Thêm/xóa placement thủ công phải cập nhật số lượng còn lại, danh sách chưa đặt, sheet list, lựa chọn và preview đồng bộ. Xóa toàn bộ nesting phải giữ nguyên Parts; xóa Part nguồn là thao tác riêng.
10. Di chuyển nhóm placement phải kiểm tra toàn nhóm như một giao dịch: loại toàn bộ thành viên nhóm khỏi danh sách vật cản, rồi kiểm tra từng placement mới với phần còn lại và với các thành viên đã kiểm tra. Không áp dụng một phần nếu bất kỳ thành viên nào lỗi.
11. Chọn nhóm bằng Ctrl-click hoặc quét vùng chỉ thay đổi selection UI; mọi move/delete trên nhóm phải làm việc với toàn bộ selection và giữ chỉ số hợp lệ sau khi scene/sheet thay đổi.
12. Di chuyển bằng mũi tên phải tiến tới vật cản hoặc mép tấm theo một trục; `Shift` là bước đúng 1 mm. Tối ưu độ trễ không được bỏ qua kiểm tra cuối cùng hoặc làm giảm độ chính xác placement.
13. Các lệnh quản lý placement thủ công (+ sheet, xóa chọn, xóa toàn bộ kết quả) thuộc context menu của vùng **Kết quả Nesting**, không đưa trở lại thanh công cụ chung nếu chưa có yêu cầu UX rõ ràng.
14. Thiết lập thuật toán nesting tập trung tại tab **Thiết lập** ở thanh điều hướng. Không tạo lại modal thiết lập trùng lặp hoặc thêm nút nhanh tự động xoay vào toolbar chính.
15. Nhập Part, quản lý Sheet và xóa Part nguồn thuộc các trang quản lý tương ứng; tránh thêm lại cụm nút nhập/xóa chung vào toolbar chính.

## 6. Mã nguồn và chất lượng

- Dùng C# nullable reference types; tránh `null!`, `dynamic` và catch tổng quát trừ khi có lý do cụ thể tại ranh giới tin cậy.
- Ưu tiên tên dễ hiểu, hàm có trách nhiệm đơn, mô hình bất biến cho request/result khi phù hợp; tránh abstraction chỉ để tăng số lớp.
- Không thêm NuGet dependency nếu .NET framework hiện có đáp ứng; nếu thêm, nêu lý do, giấy phép/tác động triển khai và bổ sung kiểm soát phiên bản thích hợp.
- Không commit/generated source hoặc binaries (`bin`, `obj`, `.vs`, `out`, `TestResults`) trừ khi người dùng yêu cầu artifact đó.
- Không để dead code, option không còn dùng, tài liệu sai, sample chỉ phục vụ tính năng đã bị bỏ, hoặc project orphan trong solution.
- Lỗi được xử lý tại ranh giới phù hợp: phân biệt lỗi dữ liệu/IO, hủy tác vụ và lỗi lập trình; không che lỗi bất ngờ bằng catch rồi tiếp tục như thành công.
- File `.nest2d` và cấu hình người dùng phải được ghi theo kiểu an toàn (file tạm cùng thư mục rồi thay thế); lỗi ghi phải được hiển thị hoặc trả về cho người dùng, không được biến thành thành công im lặng. Từ chối schema tương lai chưa hỗ trợ thay vì nạp bằng giá trị mặc định.
- Trước khi thêm helper hoặc sửa code chung, tìm nơi dùng hiện có để tránh nhân đôi logic.

## 7. Quy tắc kiểm thử

- Khi sửa code có thể chạy được, thêm/điều chỉnh test hồi quy cho hành vi thay đổi; test phải kiểm tra kết quả/điều kiện chứ không chỉ xác nhận không ném exception.
- Các test hình học cần kiểm tra tọa độ/kích thước/khoảng cách cụ thể; test UI Core cần kiểm tra cả state trước/sau và cancellation/error path khi phù hợp.
- Tổ chức test theo miền/use-case để tìm và chạy riêng được; không tiếp tục phình một `Program.cs` console harness thành nơi chứa toàn bộ test. Khi chuyển sang test framework chuẩn, giữ các test hồi quy hiện có và thêm test discovery trên CI.
- Khi sửa `RasterNester`/`NestEngine`, chạy benchmark Release `tests/CycloneNest2D.Performance` với workload tổng hợp và DXF đại diện; so sánh cùng máy, cấu hình, ngân sách và nhiều lần lặp. Báo riêng thời gian engine/validator, chất lượng, allocations và lỗi validator; không đặt ngưỡng thời gian tuyệt đối trong unit test. Dùng profiler trước khi kết luận nguyên nhân hot path.
- Kiểm thử export bằng round-trip parser là hữu ích nhưng **không chứng minh** tương thích với AutoCAD/ZWCAD/Inventor. Ghi rõ cấp độ kiểm chứng.
- Khi task yêu cầu không chạy test, **KHÔNG ĐƯỢC chạy test**. Có thể kiểm tra tĩnh như `grep`, tham chiếu project, parse XML/solution, diff và cấu trúc file; phải nói rõ test chưa chạy.
- Hai project `CycloneNest2D.Tests` và `CycloneNest2D.UI.Tests` dùng xUnit, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` và `coverlet.collector`. Chạy bằng `dotnet test CycloneNest2D.sln -c Release`; có thể lọc theo domain/test name hoặc chạy riêng từng project. Khi migrate/thêm test, phải giữ toàn bộ regression cases và xác nhận test discovery.
- `CycloneNest2D.Performance` vẫn là console benchmark; không đưa workload đo thời gian vào unit test hoặc biến project này thành test project.
- GitHub Actions kiểm tra Core/UI Core trên Ubuntu và Windows, build Desktop trên Windows, chạy Performance `--quick` và lưu báo cáo JSON/CSV làm artifact. Nếu sửa workflow, kiểm tra đường dẫn/đầu ra artifact và báo rõ nếu chưa có kết quả chạy từ GitHub.
- Build Desktop cần Windows target/WPF; xác minh tương tác thực tế trên Windows khi thay đổi XAML, resource, input hoặc layout có ảnh hưởng trực quan.
- Nếu môi trường thiếu SDK/runtime, không giả lập kết quả. Báo cụ thể lệnh không chạy và lý do.

## 8. Quy trình bắt buộc cho thay đổi

1. Đọc quy tắc này, README, PROJECT_GUIDE và các file trực tiếp liên quan.
2. Tìm kiếm usages, references, bindings, serialization, export path và docs bị ảnh hưởng trước khi sửa.
3. Nêu kế hoạch nhỏ, sửa theo ranh giới hiện có và không mở rộng phạm vi ngoài yêu cầu.
4. Cập nhật test/tài liệu cùng thay đổi. Giữ diff dễ review; không format hàng loạt file không liên quan.
5. Kiểm tra tham chiếu solution/project và tìm dead reference bằng tìm kiếm toàn repo.
6. Chạy đúng các bước xác minh được phép; tôn trọng yêu cầu dừng/chưa chạy test.
7. Rà diff cuối: không có secret, dữ liệu người dùng, artefact, thay đổi không liên quan hoặc tài liệu sai.
8. Báo cáo phần đã sửa, phần đã xác minh, phần chưa xác minh và rủi ro còn lại. Không tuyên bố “build sạch/test pass” nếu chưa chạy thành công.

## 9. Quy tắc tài liệu và giao tiếp

- README dành cho cài đặt/chạy và thao tác người dùng; PROJECT_GUIDE dành cho kiến trúc/luồng; DEVELOPMENT_RULES là chuẩn thực thi cho thay đổi.
- Khi thêm/xóa project, cập nhật solution, README, sơ đồ kiến trúc, publish/build docs và mọi tham chiếu liên quan.
- Không mô tả tính năng hoặc tương thích CAD đã kiểm chứng nếu chỉ suy ra từ mã nguồn.
- Viết tiếng Việt rõ ràng cho tài liệu hướng dẫn hiện tại; giữ nguyên identifier/API tiếng Anh.
- Báo cáo trung thực: phân biệt “đã đọc tĩnh”, “đã build”, “đã chạy test” và “đã xác minh trên Windows/CAD”.

## 10. Checklist trước khi bàn giao

- [ ] Không còn CLI, project orphan hoặc tham chiếu CLI nào trong source/solution/docs.
- [ ] Ranh giới Desktop → UI Core → Core được giữ nguyên.
- [ ] Geometry vẫn theo mm, không fallback contour lỗi sang hộp bao, margin/spacing vẫn độc lập.
- [ ] Validator/preflight phù hợp với thay đổi.
- [ ] Đã thêm/cập nhật test hoặc ghi rõ lý do không làm/chưa chạy.
- [ ] File dự án `.nest2d` tương thích hoặc có migration/test khi schema đổi.
- [ ] README và tài liệu kiến trúc đúng với mã hiện tại.
- [ ] Không phát sinh artefact/secret/file người dùng ngoài ý muốn.
- [ ] Báo cáo rõ xác minh nào thực sự đã thực hiện.
