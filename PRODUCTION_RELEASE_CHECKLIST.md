# Kế hoạch và checksheet phát hành sản xuất

Tài liệu này chuyển các rủi ro đã rà soát thành thứ tự xử lý và tiêu chí quyết định phát hành. Đây là mẫu dùng cho từng phiên bản; ghi lại bằng chứng thực tế, không đánh dấu đạt chỉ vì một bước đã được cấu hình.

## Đánh giá hiện tại

**Trạng thái: NO-GO cho phát hành sản xuất rộng rãi.** Lần rà soát gần nhất ghi nhận:

- Build Desktop Release thành công; 132/132 unit/UI-core tests đạt.
- `dotnet format --verify-no-changes` báo 55 lỗi whitespace. Phần lớn nằm ngoài thay đổi đang làm; cần xác nhận lại với đúng SDK của CI trước khi sửa để tránh format lan rộng không cần thiết.
- Desktop có dependency bắc cầu `System.Security.Cryptography.Pkcs 8.0.1`, được NuGet đánh dấu có advisory High và Moderate. Advisory công bố bản vá 8.0.2 cho nhánh 8.0. Chưa xác nhận mã ứng dụng gọi API bị ảnh hưởng; vẫn phải xử lý dependency trước phát hành.
- Ứng dụng nhắm .NET 8. Theo lịch hỗ trợ tại thời điểm rà soát 05/11/2026, .NET 8 hết hỗ trợ ngày 10/11/2026.
- Bộ đọc DXF nạp toàn bộ file vào bộ nhớ và mở rộng mảng INSERT theo số hàng/cột của file. Độ sâu block có giới hạn nhưng chưa thấy ngân sách tổng thể cho số entity sau mở rộng.
- CI hiện kiểm thử Core/UI Core và build Desktop; chưa tạo gói cài đặt phát hành và chưa thay thế xác minh thao tác WPF/CAM trên máy người dùng.

## Kế hoạch xử lý theo giai đoạn

| Giai đoạn | Mức | Công việc | Điều kiện hoàn tất |
|---|---|---|---|
| 0. Chốt phạm vi | Bắt buộc | Tạo nhánh phát hành từ commit rõ ràng; ghi nhận riêng các thay đổi DXF và file thử nghiệm chưa commit. Chỉ đưa vào bản phát hành những thay đổi đã review, test và được chấp thuận. | Có commit/tag ứng viên phát hành; working tree và danh sách file đưa vào gói được kiểm tra; không phát hành nhầm file thử nghiệm hoặc thay đổi chưa duyệt. |
| 1. Gỡ chặn dependency | Bắt buộc trước mọi phát hành | Xác định nguồn `System.Security.Cryptography.Pkcs` trong dependency graph của PDFsharp. Ưu tiên bản vá thấp nhất không còn advisory và tương thích TFM; nếu không thể nâng trực tiếp, nâng dependency gốc hoặc thư viện cha. Không che cảnh báo bằng suppression. | Restore sạch; `dotnet list package --vulnerable --include-transitive` không còn advisory chưa được chấp thuận trong dependency runtime; build, test và publish đều dùng graph đã kiểm tra. |
| 2. Giới hạn đầu vào DXF | Bắt buộc trước khi nhận file không tin cậy | Định nghĩa ngưỡng theo dữ liệu khách hàng thực tế cho dung lượng file, số record/entity, số điểm và tổng số entity/instance sau INSERT. Kiểm tra số hàng/cột, tích số lượng trước vòng lặp; dừng với thông báo lỗi rõ ràng khi vượt ngưỡng. Kiểm tra giá trị không hữu hạn, số học tràn và nesting block lồng nhau. | Có test cho ngay dưới ngưỡng, đúng ngưỡng, vượt ngưỡng và DXF hỏng/cực lớn; đầu vào vượt ngưỡng bị từ chối có kiểm soát, không treo, không tăng bộ nhớ không giới hạn và không tạo kết quả sai. Ngưỡng được ghi trong tài liệu người dùng. |
| 3. Nâng nền tảng .NET | Bắt buộc cho phát hành dài hạn | Chuyển các project liên quan sang .NET 10 LTS sau khi kiểm kê tương thích PDFsharp/WPF, test và môi trường triển khai; cập nhật README, CI, SDK dùng trong phát triển và runtime trong gói cài đặt. Tránh trộn TFM hoặc SDK không chủ ý giữa các project. | Build/test/format/publish trên Windows bằng SDK đã chốt; xác minh ứng dụng trên máy sạch có runtime được hỗ trợ; không còn dependency/framework ngoài hỗ trợ. |
| 4. Làm xanh CI và hoàn thiện gói phát hành | Bắt buộc | Chạy format bằng đúng SDK mà CI sử dụng. Sửa các lỗi format cần thiết theo diff nhỏ; không format hàng loạt file ngoài phạm vi. Thêm bước publish artifact có thể tái lập, kiểm tra đầu ra; chuẩn hóa version, release notes, hash và quy trình ký số. Chọn rõ framework-dependent hay self-contained và cung cấp hướng dẫn cài đặt/gỡ cài đặt. | Tất cả workflow bắt buộc xanh trên commit/tag phát hành; artifact cài được trên máy Windows sạch; phiên bản/hash khớp release; xác minh chữ ký nếu phân phối có ký số. |
| 5. Nghiệm thu nghiệp vụ và thí điểm | Bắt buộc | Chạy ma trận DXF thật từ các nguồn được hỗ trợ: đơn vị khác nhau, layer, cung/lỗ, spline, block, chữ/MTEXT, xoay/lật, file lỗi. Đối chiếu nesting, lưu/mở dự án, DXF/PDF xuất và import vào phần mềm CAM thực tế. Kiểm tra thao tác WPF trên Windows. | Người phụ trách nghiệp vụ/CAM ký xác nhận kết quả và dung sai; không còn lỗi nghiêm trọng/cao chưa xử lý; đạt ngưỡng hiệu năng đã thống nhất; hoàn tất thí điểm và có cách quay lui/hỗ trợ người dùng. |

**Thứ tự khuyến nghị:** giai đoạn 0 → 1 và 2 song song → 3 → 4 → 5. Giai đoạn 1/2 là chặn phát hành; giai đoạn 3 cần hoàn tất trước phát hành dài hạn vì thời điểm hết hỗ trợ .NET 8 đã cận kề. Nếu cần bản thử nghiệm có thời hạn ngắn trước khi nâng nền tảng, phải có phê duyệt rủi ro bằng văn bản, cập nhật .NET 8 mới nhất, giới hạn đối tượng nhận và kế hoạch nâng cấp; không xem đó là ngoại lệ mặc định cho sản xuất.

## Checksheet cho từng bản phát hành

**Quy ước:** chỉ phát hành khi tất cả mục bắt buộc đạt. `N/A` phải có lý do và người có thẩm quyền duyệt. Đính kèm liên kết đến log CI, artifact, báo cáo test hoặc biên bản; không chỉ ghi “đã kiểm tra”.

### Thông tin phiên bản

| Trường | Giá trị |
|---|---|
| Phiên bản / tag | |
| Commit SHA | |
| Ngày phát hành | |
| Người phụ trách phát hành | |
| Người nghiệm thu nghiệp vụ/CAM | |
| Artifact, SHA-256 | |
| Môi trường và SDK/runtime | |

### A. Mã nguồn, build và dependency

| Đạt | Điều kiện bắt buộc | Bằng chứng / ghi chú |
|---|---|---|
| [ ] | Phát hành từ commit/tag đã review; danh sách file trong artifact không chứa dữ liệu thử nghiệm, file DXF khách hàng hoặc thay đổi chưa được duyệt. | |
| [ ] | CI bắt buộc xanh trên đúng commit: format, Core/UI tests, End-to-End đã cấu hình và build Desktop Windows. | |
| [ ] | Test Release toàn solution đạt; số pass/fail/skip và log được lưu. Mọi skip/failure đều được xem xét, không bỏ qua lỗi để phát hành. | |
| [ ] | Restore/publish sạch và dependency runtime được quét; không có advisory chưa xử lý. Advisory được chấp nhận ngoại lệ phải có phân tích tác động, biện pháp giảm thiểu, người duyệt và ngày hết hạn. | |
| [ ] | TFM, SDK, runtime và package đều còn được hỗ trợ; bản .NET được vá mới nhất trong nhánh hỗ trợ. | |
| [ ] | Có xác nhận license/quyền phân phối cho ứng dụng, icon, thư viện và nội dung được đóng gói. | |

### B. DXF, hình học và hiệu năng

| Đạt | Điều kiện bắt buộc | Bằng chứng / ghi chú |
|---|---|---|
| [ ] | Các giới hạn dung lượng, record/entity, điểm và tổng instance sau INSERT được đặt, kiểm tra trước khi cấp phát/mở rộng, và công bố cho người dùng. | |
| [ ] | DXF vượt giới hạn hoặc sai cấu trúc được báo lỗi rõ ràng; test chứng minh ứng dụng không treo, không cạn bộ nhớ và không xuất kết quả một phần gây hiểu nhầm. | |
| [ ] | Bộ DXF đại diện từ từng nguồn CAD được nhập; kiểm tra đơn vị, layer, contour/lỗ, cung/spline, INSERT, TEXT/MTEXT, cảnh báo và chi tiết không xếp được. | |
| [ ] | Bộ kết quả nesting được Validator chấp nhận; kiểm tra kích thước, khoảng cách, lề, xoay/lật và vị trí thủ công. Dung sai nghiệm thu do nghiệp vụ/CAM ghi rõ trước khi chạy. | |
| [ ] | DXF/PDF xuất mở lại được; các tính năng xuất đang bật (micro-joints, xóa cạnh chung, block nếu dùng) được kiểm chứng trong phần mềm CAM mục tiêu trước sản xuất. | |
| [ ] | Benchmark dùng bộ dữ liệu chuẩn đạt ngưỡng thời gian/bộ nhớ đã được nghiệp vụ thống nhất; lưu báo cáo và so sánh với baseline. | |

### C. Dự án, lưu trữ và trải nghiệm vận hành

| Đạt | Điều kiện bắt buộc | Bằng chứng / ghi chú |
|---|---|---|
| [ ] | Trên máy Windows sạch, cài đặt/chạy ứng dụng theo đúng hướng dẫn; kiểm tra runtime, quyền ghi, mở DXF/dự án, lưu/mở lại, xuất DXF/PDF và gỡ cài đặt. | |
| [ ] | Mở dự án cũ và migration định dạng được kiểm thử; dữ liệu gốc được sao lưu trước migration và có thể phục hồi. | |
| [ ] | Lỗi đọc/ghi, thiếu DXF tham chiếu, hết dung lượng hoặc không đủ quyền được thông báo; không làm mất dữ liệu dự án âm thầm. | |
| [ ] | Người dùng xác nhận UI WPF thực tế trên Windows: scaling/DPI, độ phân giải hỗ trợ, nhập số theo locale, kéo thả, thao tác bàn phím/chuột, hộp thoại lỗi và khả năng tiếp cận cơ bản. | |
| [ ] | Log chẩn đoán không ghi dữ liệu nhạy cảm ngoài chủ đích; tài liệu hỗ trợ nêu rõ vị trí log và cách gửi báo cáo sự cố an toàn. | |

### D. Đóng gói, phát hành và phê duyệt

| Đạt | Điều kiện bắt buộc | Bằng chứng / ghi chú |
|---|---|---|
| [ ] | Gói cài đặt có tên sản phẩm, phiên bản, publisher và kiến trúc đúng; xác định rõ yêu cầu .NET/runtime hoặc đóng gói self-contained. | |
| [ ] | Artifact được cài và chạy thử trên máy không có SDK/IDE; không phụ thuộc đường dẫn hoặc file chỉ tồn tại trên máy phát triển. | |
| [ ] | Artifact phát hành bất biến, có SHA-256; chữ ký số hợp lệ và publisher được xác minh nếu chính sách phân phối yêu cầu ký. | |
| [ ] | Release notes nêu thay đổi, hạn chế, yêu cầu hệ thống, cách nâng cấp và cách quay lui; kênh tải xuống là kênh được phê duyệt. | |
| [ ] | Có bản sao lưu/khôi phục dữ liệu, đầu mối hỗ trợ và quyết định xử lý khi cần thu hồi bản phát hành. | |
| [ ] | Chủ sản phẩm và người nghiệm thu nghiệp vụ/CAM xác nhận **GO**; mọi rủi ro còn mở có người chịu trách nhiệm và phê duyệt. | |

**Quyết định:** [ ] GO  [ ] NO-GO  
**Lý do / điều kiện còn mở:**  

**Chủ sản phẩm:** ____________________  **Ngày:** __________  

**Nghiệm thu nghiệp vụ/CAM:** ____________________  **Ngày:** __________

## Tham chiếu

- [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [GHSA-846f-f764-76mx: System.Security.Cryptography.Pkcs](https://github.com/advisories/GHSA-846f-f764-76mx)
- [GHSA-2fmv-rv2c-59x7: System.Security.Cryptography.Pkcs](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)
