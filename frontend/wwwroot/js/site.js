document.querySelectorAll("[data-password-toggle]").forEach((button) => {
    button.addEventListener("click", () => {
        const input = document.getElementById(button.dataset.passwordToggle);
        if (!input) {
            return;
        }

        const showPassword = input.type === "password";
        input.type = showPassword ? "text" : "password";
        button.textContent = showPassword ? "Hide" : "Show";
        button.setAttribute("aria-label", showPassword ? "Hide password" : "Show password");
    });
});

document.querySelectorAll(".pos-quantity-control").forEach((control) => {
    const quantityInput = control.querySelector('input[type="number"]');
    control.addEventListener("click", (event) => {
        const button = event.target.closest("[data-quantity-minus], [data-quantity-plus]");
        if (!button) {
            return;
        }

        const minimum = Number.parseInt(quantityInput.min, 10) || 1;
        const maximum = Number.parseInt(quantityInput.max, 10) || Number.MAX_SAFE_INTEGER;
        const current = Number.parseInt(quantityInput.value, 10) || minimum;
        const change = button.hasAttribute("data-quantity-plus") ? 1 : -1;
        quantityInput.value = String(Math.min(maximum, Math.max(minimum, current + change)));
    });

    quantityInput.addEventListener("input", () => {
        const maximum = Number.parseInt(quantityInput.max, 10);
        const value = Number.parseInt(quantityInput.value, 10);
        if (Number.isFinite(value) && Number.isFinite(maximum) && value > maximum) {
            quantityInput.value = String(maximum);
        }
    });
});
