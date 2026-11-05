# Kế hoạch và checksheet phát hành sản xuất

Tài liệu này chuyển các rủi ro đã rà soát thành thứ tự xử lý và tiêu chí quyết định phát hành. Đây là mẫu dùng cho từng phiên bản; ghi lại bằng chứng thực tế, không đánh dấu đạt chỉ vì một bước đã được cấu hình.

## Đánh giá hiện tại

**Trạng thái: NO-GO cho phát hành sản xuất rộng rãi.** Các chặn kỹ thuật trong mã nguồn đã được xử lý trong working tree; kết quả dưới đây là kiểm tra cục bộ, chưa phải CI trên commit phát hành hay nghiệm thu sản xuất:

- Đã nâng các project lên .NET 10; build Release thành công, 0 cảnh báo/0 lỗi.
- Đã chạy 141/141 test (71 Core, 70 UI Core) và `dotnet format --verify-no-changes` đạt.
- Desktop dependency audit hiện không báo package có lỗ hổng. `System.Security.Cryptography.Pkcs` được cung cấp bởi .NET 10 framework nên không ép reference bản 8.x.
- DXF có giới hạn dung lượng/dòng/cặp group code, entity sau mở rộng, INSERT, điểm polyline/spline và đoạn xấp xỉ ellipse; có regression test cho các ngưỡng này.
- Publish Windows x64 framework-dependent và tạo manifest SHA-256 đã chạy thành công cục bộ. Artifact trên GitHub Actions chưa được kiểm chứng; đây không phải installer đã ký số.
- Chưa xác minh thao tác WPF trên máy người dùng, DXF/CAM với phần mềm đích, giấy phép/quyền phân phối, chữ ký số hoặc phê duyệt của chủ sản phẩm.

## Kế hoạch xử lý theo giai đoạn

| Giai đoạn | Mức | Công việc | Điều kiện hoàn tất |
|---|---|---|---|
| 0. Chốt phạm vi | Bắt buộc | Tạo nhánh phát hành từ commit rõ ràng; ghi nhận riêng các thay đổi DXF và file thử nghiệm chưa commit. Chỉ đưa vào bản phát hành những thay đổi đã review, test và được chấp thuận. | Có commit/tag ứng viên phát hành; working tree và danh sách file đưa vào gói được kiểm tra; không phát hành nhầm file thử nghiệm hoặc thay đổi chưa duyệt. |
| 1. Gỡ chặn dependency | Đã xử lý cục bộ | Chuyển sang .NET 10 framework package graph và quét dependency runtime; không thêm direct package reference không cần thiết. | `dotnet list package --vulnerable --include-transitive` không báo package dễ tổn thương; lặp lại trên CI và commit phát hành. |
| 2. Giới hạn đầu vào DXF | Đã xử lý cục bộ; cần kiểm chứng dữ liệu thật | Áp giới hạn file 32 MiB/1,2 triệu dòng/500.000 cặp group code, 250.000 entity mở rộng, 50.000 instance INSERT, 50.000 điểm polyline, 4.096 điểm spline và 100.000 đoạn ellipse. | Regression test các ngưỡng dung lượng, dòng, group code, entity mở rộng, INSERT, polyline, spline/bậc và ellipse đạt; xác nhận ngưỡng phù hợp file khách hàng thực tế; tiếp tục kiểm thử malformed DXF trước phát hành. |
| 3. Nâng nền tảng .NET | Đã xử lý cục bộ | Chuyển project source/test/benchmark, CI, tài liệu và runtime hướng dẫn sang .NET 10; xác minh PDFsharp/WPF tương thích. | Build/test/format/publish Windows x64 đạt cục bộ; chạy CI trên commit và cài/chạy trên máy Windows sạch trước GO. |
| 4. Làm xanh CI và hoàn thiện gói phát hành | Một phần | Format CI, publish framework-dependent win-x64 và tạo manifest SHA-256 đã cấu hình; cần kiểm tra artifact GitHub, chốt release notes và chọn/ký installer nếu quy trình phát hành yêu cầu. | Workflow xanh trên tag/commit phát hành; artifact cài được trên máy sạch; phiên bản/hash khớp; xác minh chữ ký nếu phân phối có ký số. |
| 5. Nghiệm thu nghiệp vụ và thí điểm | Bắt buộc | Chạy ma trận DXF thật từ các nguồn được hỗ trợ: đơn vị khác nhau, layer, cung/lỗ, spline, block, chữ/MTEXT, xoay/lật, file lỗi. Đối chiếu nesting, lưu/mở dự án, DXF/PDF xuất và import vào phần mềm CAM thực tế. Kiểm tra thao tác WPF trên Windows. | Người phụ trách nghiệp vụ/CAM ký xác nhận kết quả và dung sai; không còn lỗi nghiêm trọng/cao chưa xử lý; đạt ngưỡng hiệu năng đã thống nhất; hoàn tất thí điểm và có cách quay lui/hỗ trợ người dùng. |

**Thứ tự khuyến nghị:** giai đoạn 0 → 1 và 2 song song → 3 → 4 → 5. Những công việc kỹ thuật 1–3 đã có trong working tree; chúng chỉ được xem là đã hoàn tất sau khi CI chạy trên commit và ngưỡng DXF được nghiệp vụ xác nhận. Chưa phát hành production đến khi hoàn thành các bước kiểm tra máy sạch, ký/phân phối phù hợp và nghiệm thu giai đoạn 5.

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
