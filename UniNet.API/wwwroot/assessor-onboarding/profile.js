const form = document.querySelector("#profile");
const message = document.querySelector("#message");
const email = document.querySelector("#email");
const submit = form.querySelector("button");
const token = new URLSearchParams(location.hash.slice(1)).get("token");
history.replaceState(null, "", location.pathname);

async function post(path, data) {
    return fetch(path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        cache: "no-store",
        credentials: "omit",
        body: JSON.stringify(data)
    });
}

async function openForm() {
    if (!token) {
        message.textContent = "Vui lòng mở lại đường dẫn trong lời mời.";
        return;
    }
    try {
        const response = await post("/api/assessor-onboarding/profile/inspect", { token });
        if (!response.ok) {
            message.textContent = response.status === 410 || response.status === 400
                ? "Lời mời không hợp lệ hoặc đã hết hiệu lực."
                : "Không thể mở hồ sơ lúc này. Vui lòng mở lại lời mời sau.";
            return;
        }
        const detail = await response.json();
        if (detail.contractLink) {
            location.replace(detail.contractLink);
            return;
        }
        email.value = detail.email;
        message.textContent = "";
        form.hidden = false;
    } catch {
        message.textContent = "Không thể kết nối. Vui lòng mở lại lời mời sau.";
    }
}

form.addEventListener("submit", async (event) => {
    event.preventDefault();
    submit.disabled = true;
    try {
        const profile = Object.fromEntries(new FormData(form));
        const response = await post("/api/assessor-onboarding/profile", { ...profile, token });
        if (response.ok) {
            const detail = await response.json();
            form.reset();
            form.hidden = true;
            location.replace(detail.contractLink);
        } else if (response.status === 410) {
            form.reset();
            form.hidden = true;
            message.textContent = "Lời mời không còn hiệu lực hoặc hồ sơ đã được nộp.";
        } else {
            const error = await response.json().catch(() => ({}));
            message.textContent = error.message || (response.status === 400
                ? "Thông tin chưa hợp lệ. Vui lòng kiểm tra các trường và gửi lại."
                : "Chưa thể nộp hồ sơ. Vui lòng thử lại.");
        }
    } catch {
        message.textContent = "Không thể kết nối. Vui lòng thử lại.";
    } finally {
        submit.disabled = false;
    }
});

openForm();
