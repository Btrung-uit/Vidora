# Checklist bạn cần thực hiện trước khi phát hành

Đích đầu tiên: portable ZIP bản gọn 0.1.0-beta.1, Windows 11 x64.
Không cần .NET SDK hay Inno Setup để thử ZIP. Dùng máy khác, máy ảo hoặc Windows
Sandbox có Internet; không lấy máy phát triển hiện tại làm bằng chứng máy sạch.

## 1. Đúng gói

- Lấy ZIP và .zip.sha256 của đúng commit từ CI/PR đã duyệt; không dùng ZIP cũ ngày 05/11.
- Chạy `Get-FileHash <file.zip> -Algorithm SHA256`, so với checksum.
- Giải nén toàn bộ vào đường dẫn có dấu/khoảng trắng, ví dụ `C:\Users\Public\Video thử\Vidora`.
- Mở build-manifest.json, ghi version/sourceCommit; sourceDirty phải false.
- Chạy Vidora.exe. Gói chỉ kèm .NET runtime, không có FFmpeg/yt-dlp.

## 2. Thiết lập lần đầu

- Banner thiếu công cụ dẫn đến Cài đặt; mọi nút cuộn tới được.
- Cài yt-dlp bằng nút trong app: thấy trạng thái đang làm, rồi phiên bản/sẵn sàng.
- Lấy FFmpeg Windows từ nhà cung cấp được [ffmpeg.org](https://ffmpeg.org/download.html) liên kết.
  Giải nén riêng, chọn bin/ffmpeg.exe trong Cài đặt; giữ nguyên thư mục đó.
- Thử file không hợp lệ: báo lỗi rõ, giữ cấu hình cũ.
- Thoát/mở lại: giữ đường dẫn FFmpeg, giao diện và thư mục lưu.
- Thử mất mạng: báo lỗi, cho thử lại, không treo/mất công cụ đang dùng.

## 3. Video mẫu bạn có quyền tải

Cần ít nhất một video công khai ngắn trên mỗi nền tảng muốn tuyên bố hỗ trợ:
YouTube, Facebook, Instagram, TikTok. Không gửi mật khẩu/cookie.

Ghi URL, thời điểm, phiên bản yt-dlp/FFmpeg, chất lượng và kết quả/lỗi vào
platform-test-matrix.md. File phải phát được, có hình/tiếng. Thử 720p và một chất
lượng cần ghép hình/tiếng khi nguồn cung cấp. Không commit media vào repo.

## 4. Luồng sử dụng

- Thêm hai video, tải lần lượt; hủy rồi tiếp tục hàng đợi.
- Thử lại/xóa mục lỗi; lịch sử giữ lần thử trước.
- Dọn mục kết thúc: giữ mục đang tải/chờ và file đã tải.
- Mở file/thư mục từ hàng đợi/lịch sử; xóa file bằng Explorer rồi kiểm tra báo thiếu.
- Tìm/lọc lịch sử kết hợp; không có kết quả; có lượt tải mới.
- Thư mục không có quyền ghi, trùng tên, đóng app khi đang tải, mở lại hàng đợi.
- Tab/Shift+Tab/Enter và focus; cửa sổ tối thiểu; DPI125% và 150%.
- Theo Windows: đổi sáng/tối khi app đang mở; bật/tắt Contrast theme. Chữ/nút/vùng
  chọn vẫn đọc được. Nếu có thể, thử Narrator đọc tên control.

## 5. Quyết định của bạn

Đã chốt ngày 2026-11-06: chủ dự án giữ tên **Vidora** sau khi biết có sản phẩm khác trùng tên; đây không phải xác nhận quyền nhãn hiệu.

- Chốt tên: đã có [Vidora trên App Store](https://apps.apple.com/us/app/vidora-ai-video-generator/id6748969599).
  Đây là phát hiện trùng tên, chưa phải kết luận nhãn hiệu. Nếu đổi tên, làm trước
  tag/installer identity; không chỉ đổi tên ZIP.
- Duyệt PR, xem CI xanh, chốt nền tảng đã thử và các hạn chế.
- Xác nhận khi muốn tạo GitHub **prerelease**; chưa gọi là stable/1.0.

Bạn chỉ cần gửi lại môi trường Windows, commit/version, kết quả từng mục, các URL
mẫu được phép thử và ảnh/thông báo lỗi. Không cần tự sửa code.
