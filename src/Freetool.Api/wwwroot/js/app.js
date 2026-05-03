(() => {
  function closest(target, selector) {
    return target instanceof Element ? target.closest(selector) : null;
  }

  function updatePermissionsSaveState(container) {
    const saveButton = container.querySelector("[data-permissions-save]");
    if (!(saveButton instanceof HTMLButtonElement)) return;

    const checkboxes = Array.from(
      container.querySelectorAll("[data-permission-checkbox]")
    );

    const hasChanges = checkboxes.some((checkbox) => {
      if (!(checkbox instanceof HTMLInputElement)) return false;
      const initialValue = checkbox.getAttribute("data-initial-checked") === "true";
      return checkbox.checked !== initialValue;
    });

    saveButton.disabled = !hasChanges;
  }

  function initializePermissionsMatrices() {
    document
      .querySelectorAll("[data-permissions-matrix]")
      .forEach(updatePermissionsSaveState);
  }

  function updateTypedConfirmState(form) {
    const input = form.querySelector("[data-confirm-input]");
    const submitButton = form.querySelector("[data-confirm-submit]");
    if (!(input instanceof HTMLInputElement)) return;
    if (!(submitButton instanceof HTMLButtonElement)) return;

    const expected = input.getAttribute("data-confirm-expected") || "";
    submitButton.disabled = input.value.trim() !== expected;
  }

  function initializeTypedConfirmForms() {
    document
      .querySelectorAll("[data-typed-confirm-form]")
      .forEach(updateTypedConfirmState);
  }

  document.addEventListener("input", (event) => {
    const input = closest(event.target, "[data-confirm-input]");
    if (!input) return;

    const form = input.closest("[data-typed-confirm-form]");
    if (form) updateTypedConfirmState(form);
  });

  document.addEventListener("change", (event) => {
    const checkbox = closest(event.target, "[data-permission-checkbox]");
    if (!checkbox) return;

    const container = checkbox.closest("[data-permissions-matrix]");
    if (container) updatePermissionsSaveState(container);
  });

  document.addEventListener("click", (event) => {
    const target = event.target;
    const addButton = closest(target, "[data-add-kv-row]");
    if (addButton) {
      const container = addButton.closest("[data-kv-rows]");
      const template = container?.querySelector(".kv-row-template");
      if (container && template) {
        const clone = template.cloneNode(true);
        clone.classList.remove("kv-row-template");
        container.insertBefore(clone, addButton);
      }
      return;
    }

    const removeButton = closest(target, "[data-remove-row]");
    if (removeButton) {
      const row = removeButton.closest(".kv-row");
      if (row && !row.classList.contains("kv-row-template")) row.remove();
      return;
    }

    const modalOpen = closest(target, "[data-modal-open]");
    if (modalOpen) {
      event.preventDefault();
      const modalId = modalOpen.getAttribute("data-modal-open");
      const modal = modalId ? document.getElementById(modalId) : null;
      if (modal && typeof modal.showModal === "function") modal.showModal();
      return;
    }

    const modalClose = closest(target, "[data-modal-close]");
    if (modalClose) {
      event.preventDefault();
      const modal = modalClose.closest("dialog");
      if (modal && typeof modal.close === "function") modal.close();
      return;
    }

    const confirmElement = closest(target, "[data-confirm]");
    if (confirmElement) {
      const message = confirmElement.getAttribute("data-confirm") || "Are you sure?";
      if (!window.confirm(message)) event.preventDefault();
    }
  });

  initializePermissionsMatrices();
  initializeTypedConfirmForms();
})();
