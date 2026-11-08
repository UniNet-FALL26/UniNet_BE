const token = new URLSearchParams(location.hash.slice(1)).get("token");
history.replaceState(null, "", location.pathname);
const form = document.querySelector("#password");
const message = document.querySelector("#message");
const resend = document.querySelector("#resend");

async function post(path, data) {
    const response = await fetch("/api/assessor-onboarding/password" + path, {
        method: "POST", headers: { "Content-Type": "application/json" },
        credentials: "omit", cache: "no-store", body: JSON.stringify(data)
    });
    if (!response.ok) {
        if (response.status === 429) throw new Error("Vui lòng chờ trước khi thử lại. Tối đa 3 email kích hoạt mỗi giờ.");
        const error = await response.json().catch(() => ({}));
        throw new Error(error.message || "Không thể thực hiện. Vui lòng kiểm tra thông tin và hạn của đường dẫn.");
    }
    return response;
}

async function open() {
    if (!token) {
        message.textContent = "Vui lòng mở lại đường dẫn kích hoạt trong email.";
        return;
    }
    resend.hidden = false;
    try {
        const detail = await (await post("/inspect", { token })).json();
        document.querySelector("#identity").textContent = detail.fullName + " — " + detail.emailNormalized;
        message.textContent = "Đặt mật khẩu để hoàn tất kích hoạt tài khoản của bạn.";
        form.hidden = false;
    } catch (error) { message.textContent = error.message; }
}

form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    try {
        const fields = Object.fromEntries(new FormData(form));
        await post("", { token, ...fields });
        form.reset();
        form.hidden = true;
        resend.hidden = true;
        message.textContent = "Tài khoản đã được kích hoạt. Bạn có thể đăng nhập UniNet bằng email và mật khẩu vừa đặt.";
    } catch (error) { message.textContent = error.message; }
    finally { button.disabled = false; }
});

resend.addEventListener("click", async () => {
    resend.disabled = true;
    try {
        await post("/resend", { token });
        form.reset();
        form.hidden = true;
        message.textContent = "Yêu cầu đã được tiếp nhận. Nếu hồ sơ đủ điều kiện, link mới sẽ được gửi qua email. Vui lòng sử dụng email mới nhất.";
    } catch (error) { message.textContent = error.message; }
    finally { resend.disabled = false; }
});

open();
