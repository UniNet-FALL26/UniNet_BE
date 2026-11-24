const message = document.querySelector("#message");
const panel = document.querySelector("#contract");
const form = document.querySelector("#sign");
const token = new URLSearchParams(location.hash.slice(1)).get("token");
history.replaceState(null, "", location.pathname);
let detail;

async function post(path, data = {}) {
    const response = await fetch("/api/assessor-onboarding/contract/" + path, {
        method: "POST", headers: { "Content-Type": "application/json" },
        credentials: "omit", cache: "no-store", body: JSON.stringify({ ...data, token })
    });
    if (!response.ok) {
        if (response.status === 410) throw new Error("Đường dẫn đã hết hiệu lực. Vui lòng liên hệ UniNet.");
        if (response.status === 429) throw new Error("Vui lòng chờ rồi thử lại.");
        const error = await response.json().catch(() => ({}));
        throw new Error(error.message || error.detail || "Chưa thể thực hiện. Vui lòng thử lại.");
    }
    return response;
}

async function run(button, action) {
    if (button) button.disabled = true;
    try { await action(); }
    catch (error) { message.textContent = error.message || "Không thể kết nối. Vui lòng thử lại."; }
    finally { if (button) button.disabled = false; }
}

function showSigned(name, time) {
    form.hidden = true;
    const signed = document.querySelector("#signed");
    signed.textContent = "Thẩm định viên đã ký: " + name + " · " + new Date(time).toLocaleString("vi-VN");
    signed.hidden = false;
}

if (!token) message.textContent = "Vui lòng mở đường dẫn từ email mời hoặc nộp hồ sơ để tiếp tục.";
else run(null, async () => {
    detail = await (await post("inspect")).json();
    const documentPanel = document.querySelector("#document");
    for (const page of detail.pages) {
        const section = document.createElement("section");
        section.textContent = page;
        documentPanel.append(section);
    }
    document.querySelector("#version").textContent = "Phiên bản " + detail.templateVersion;
    document.querySelector("#statement").textContent = detail.signatureStatement;
    form.elements.signedName.value = detail.fullName;
    panel.hidden = false;
    message.textContent = "";
    if (detail.signedAt) showSigned(detail.signedName, detail.signedAt);
});

document.querySelector("#download").addEventListener("click", event => run(event.currentTarget, async () => {
    const url = URL.createObjectURL(await (await post("pdf")).blob());
    const link = document.createElement("a");
    link.href = url;
    link.download = "UniNet-Hop-dong-tham-dinh.pdf";
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
}));

form.addEventListener("submit", event => {
    event.preventDefault();
    run(event.submitter, async () => {
        const data = new FormData(form);
        const signed = await (await post("sign", {
            signedName: data.get("signedName"), accepted: data.get("accepted") === "on",
            contractSha256: detail.contractSha256
        })).json();
        form.reset();
        showSigned(signed.signedName, signed.signedAt);
        message.textContent = "Đã ký hợp đồng. UniNet sẽ gửi email thiết lập mật khẩu sau khi Admin duyệt hồ sơ.";
    });
});
