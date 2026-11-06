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
        email.value = (await response.json()).email;
        message.textContent = "Vui lòng kiểm tra thông tin trước khi nộp.";
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
            form.reset();
            form.hidden = true;
            message.textContent = "UniNet đã nhận hồ sơ của bạn.";
        } else if (response.status === 410) {
            form.reset();
            form.hidden = true;
            message.textContent = "Lời mời không còn hiệu lực hoặc hồ sơ đã được nộp.";
        } else {
            message.textContent = response.status === 400
                ? "Thông tin chưa hợp lệ. Vui lòng kiểm tra các trường và gửi lại."
                : "Chưa thể nộp hồ sơ. Vui lòng thử lại.";
        }
    } catch {
        message.textContent = "Không thể kết nối. Vui lòng thử lại.";
    } finally {
        submit.disabled = false;
    }
});

openForm();
