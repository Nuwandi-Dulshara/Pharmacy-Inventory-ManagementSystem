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

    const posCart = document.querySelector("[data-pos-cart]");
    if (posCart) {
        const search = posCart.querySelector("#medicine-search");
        const catalogItems = [...posCart.querySelectorAll("[data-pos-medicine]")];
        const cartItems = posCart.querySelector("[data-pos-items]");
        const template = posCart.querySelector("[data-pos-row-template]");
        const totalDisplay = posCart.querySelector("[data-pos-total]");
        const formatMoney = (amount) => amount.toLocaleString(undefined, {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });

        const updateRow = (row) => {
            const quantityInput = row.querySelector("[data-pos-quantity]");
            const quantity = Number.parseInt(quantityInput.value, 10) || 0;
            const price = Number.parseFloat(row.dataset.price);
            row.querySelector("[data-pos-subtotal]").textContent = formatMoney(price * quantity);
            quantityInput.max = row.dataset.stock;
            return price * quantity;
        };

        const updateTotal = () => {
            const rows = [...cartItems.querySelectorAll("[data-pos-row]")];
            cartItems.querySelector("[data-pos-empty]")?.remove();
            if (rows.length === 0) {
                const emptyRow = document.createElement("tr");
                emptyRow.dataset.posEmpty = "";
                emptyRow.innerHTML = "<td colspan=\"5\">No medicines added.</td>";
                cartItems.append(emptyRow);
            }

            const total = rows.reduce((sum, row) => sum + updateRow(row), 0);
            totalDisplay.textContent = `LKR ${formatMoney(total)}`;
        };

        search.addEventListener("input", () => {
            const filter = search.value.trim().toLocaleLowerCase();
            catalogItems.forEach((item) => {
                item.hidden = !item.dataset.name.toLocaleLowerCase().includes(filter);
            });
        });

        posCart.addEventListener("click", (event) => {
            const addButton = event.target.closest("[data-pos-add]");
            if (addButton) {
                const medicine = addButton.closest("[data-pos-medicine]");
                const existing = [...cartItems.querySelectorAll("[data-pos-row]")]
                    .find((row) => row.dataset.id === medicine.dataset.id);
                if (existing) {
                    const quantityInput = existing.querySelector("[data-pos-quantity]");
                    quantityInput.value = Math.min(
                        Number.parseInt(quantityInput.value, 10) + 1,
                        Number.parseInt(existing.dataset.stock, 10)
                    );
                } else {
                    const row = template.content.firstElementChild.cloneNode(true);
                    const index = cartItems.querySelectorAll("[data-pos-row]").length;
                    row.dataset.id = medicine.dataset.id;
                    row.dataset.price = medicine.dataset.price;
                    row.dataset.stock = medicine.dataset.stock;
                    row.querySelector("[data-pos-name]").textContent = medicine.dataset.name;
                    row.querySelector("[data-pos-price]").textContent = formatMoney(Number.parseFloat(medicine.dataset.price));
                    row.querySelector("[data-pos-medicine-id]").name = `Input.Items[${index}].MedicineId`;
                    row.querySelector("[data-pos-medicine-id]").value = medicine.dataset.id;
                    row.querySelector("[data-pos-quantity]").name = `Input.Items[${index}].Quantity`;
                    row.querySelector("[data-pos-quantity]").max = medicine.dataset.stock;
                    cartItems.append(row);
                }
                updateTotal();
                return;
            }

            const removeButton = event.target.closest("[data-pos-remove]");
            if (removeButton) {
                removeButton.closest("[data-pos-row]").remove();
                [...cartItems.querySelectorAll("[data-pos-row]")].forEach((row, index) => {
                    row.querySelector("[data-pos-medicine-id]").name = `Input.Items[${index}].MedicineId`;
                    row.querySelector("[data-pos-quantity]").name = `Input.Items[${index}].Quantity`;
                });
                updateTotal();
            }
        });

        posCart.addEventListener("input", (event) => {
            if (event.target.matches("[data-pos-quantity]")) {
                const row = event.target.closest("[data-pos-row]");
                const stock = Number.parseInt(row.dataset.stock, 10);
                const quantity = Number.parseInt(event.target.value, 10);
                if (Number.isFinite(quantity) && quantity > stock) {
                    event.target.value = stock;
                }
                updateTotal();
            }
        });
    }
});
