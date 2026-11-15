const message = document.querySelector("#message");
const readPanel = document.querySelector("#read");
const contractPanel = document.querySelector("#contract");
const contractToken = new URLSearchParams(location.hash.slice(1)).get("token");
history.replaceState(null, "", location.pathname);
let session;
let contractHash;
let pdfUrl;

async function post(path, data) {
    const response = await fetch("/api/assessor-onboarding/contract/" + path, {
        method: "POST", headers: { "Content-Type": "application/json" },
        credentials: "omit", cache: "no-store", body: JSON.stringify(data)
    });
    if (!response.ok) {
        if (response.status === 429) throw new Error("Bạn thao tác quá nhanh hoặc đã đạt giới hạn OTP. Vui lòng chờ rồi thử lại.");
        const error = await response.json().catch(() => ({}));
        throw new Error(error.message || error.detail || "Chưa thể thực hiện. Vui lòng kiểm tra mã OTP và hạn của đường dẫn.");
    }
    return response;
}

async function run(button, action) {
    button.disabled = true;
    try { await action(); }
    catch (error) { message.textContent = error.message || "Không thể kết nối. Vui lòng thử lại."; }
    finally { button.disabled = false; }
}

if (!contractToken) {
    readPanel.hidden = true;
    message.textContent = "Vui lòng mở lại đường dẫn hợp đồng từ email.";
}

document.querySelector("#read-otp").addEventListener("click", (event) => run(event.target, async () => {
    await post("read-otp", { token: contractToken });
    message.textContent = "OTP mở hợp đồng đã được yêu cầu. Vui lòng kiểm tra email, kể cả thư rác.";
}));

document.querySelector("#verify").addEventListener("submit", (event) => {
    event.preventDefault();
    run(event.submitter, async () => {
        const code = new FormData(event.target).get("code");
        session = (await (await post("verify-read-otp", { token: contractToken, code })).json()).token;
        event.target.reset();
        const detail = await (await post("inspect", { token: session })).json();
        const pdf = await (await post("pdf", { token: session })).blob();
        if (pdfUrl) URL.revokeObjectURL(pdfUrl);
        pdfUrl = URL.createObjectURL(pdf);
        document.querySelector("#pdf").src = pdfUrl;
        document.querySelector("#download").href = pdfUrl;
        document.querySelector("#version").textContent = "Phiên bản " + detail.templateVersion;
        document.querySelector("#statement").textContent = detail.signatureStatement;
        document.querySelector("#sign [name=signedName]").value = detail.fullName;
        contractHash = detail.contractSha256;
        readPanel.hidden = true;
        contractPanel.hidden = false;
        message.textContent = "Vui lòng đọc hợp đồng trước khi yêu cầu OTP ký. Mỗi lần gửi OTP cần cách nhau ít nhất 60 giây.";
    });
});

document.querySelector("#sign-otp").addEventListener("click", (event) => run(event.target, async () => {
    await post("sign-otp", { token: session });
    message.textContent = "OTP ký đã được yêu cầu. Nhập mã mới trong email và xác nhận nội dung cam kết.";
}));

document.querySelector("#sign").addEventListener("submit", (event) => {
    event.preventDefault();
    run(event.submitter, async () => {
        const data = new FormData(event.target);
        await post("sign", {
            token: session, code: data.get("code"), signedName: data.get("signedName"),
            accepted: data.get("accepted") === "on", contractSha256: contractHash
        });
        event.target.reset();
        contractPanel.hidden = true;
        session = null;
        if (pdfUrl) URL.revokeObjectURL(pdfUrl);
        document.querySelector("#pdf").removeAttribute("src");
        document.querySelector("#download").removeAttribute("href");
        message.textContent = "Bạn đã ký xác nhận thành công. Hồ sơ đang chờ duyệt.";
    });
});

window.addEventListener("pagehide", () => { if (pdfUrl) URL.revokeObjectURL(pdfUrl); });
