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

  function resetFormControls(container) {
    container.querySelectorAll("input, textarea, select").forEach((control) => {
      if (control instanceof HTMLInputElement) {
        if (control.type === "checkbox" || control.type === "radio") {
          control.checked = control.defaultChecked;
        } else {
          control.value = control.defaultValue || "";
        }
      } else if (control instanceof HTMLTextAreaElement) {
        control.value = control.defaultValue || "";
      } else if (control instanceof HTMLSelectElement) {
        control.selectedIndex = 0;
      }
    });
  }

  function updateAppResourceSections(form) {
    const select = form.querySelector("[data-app-resource-select]");
    if (!(select instanceof HTMLSelectElement)) return;

    const selectedOption = select.options[select.selectedIndex];
    const resourceKind =
      selectedOption?.getAttribute("data-resource-kind") || "http";

    form.querySelectorAll("[data-resource-kind-section]").forEach((section) => {
      const sectionKind = section.getAttribute("data-resource-kind-section");
      section.hidden = sectionKind !== resourceKind;
    });
  }

  function updateDynamicBodySections(form) {
    const checkbox = form.querySelector("[data-dynamic-body-checkbox]");
    const useDynamic =
      checkbox instanceof HTMLInputElement ? checkbox.checked : false;

    form.querySelectorAll("[data-static-body-section]").forEach((section) => {
      section.hidden = useDynamic;
    });
    form.querySelectorAll("[data-dynamic-body-help]").forEach((section) => {
      section.hidden = !useDynamic;
    });
  }

  function initializeAppConfigForms() {
    document.querySelectorAll("[data-app-config-form]").forEach((form) => {
      updateAppResourceSections(form);
      updateDynamicBodySections(form);
    });
  }

  document.addEventListener("input", (event) => {
    const input = closest(event.target, "[data-confirm-input]");
    if (!input) return;

    const form = input.closest("[data-typed-confirm-form]");
    if (form) updateTypedConfirmState(form);
  });

  document.addEventListener("change", (event) => {
    const permissionCheckbox = closest(event.target, "[data-permission-checkbox]");
    if (permissionCheckbox) {
      const container = permissionCheckbox.closest("[data-permissions-matrix]");
      if (container) updatePermissionsSaveState(container);
      return;
    }

    const resourceSelect = closest(event.target, "[data-app-resource-select]");
    if (resourceSelect) {
      const form = resourceSelect.closest("[data-app-config-form]");
      if (form) updateAppResourceSections(form);
      return;
    }

    const dynamicBodyCheckbox = closest(event.target, "[data-dynamic-body-checkbox]");
    if (dynamicBodyCheckbox) {
      const form = dynamicBodyCheckbox.closest("[data-app-config-form]");
      if (form) updateDynamicBodySections(form);
    }
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
        resetFormControls(clone);
        container.insertBefore(clone, addButton);
      }
      return;
    }

    const addInputButton = closest(target, "[data-add-input-row]");
    if (addInputButton) {
      const container = addInputButton.closest("[data-input-rows]");
      const template = container?.querySelector(".input-row-template");
      if (container && template) {
        const clone = template.cloneNode(true);
        clone.classList.remove("input-row-template");
        resetFormControls(clone);
        container.insertBefore(clone, addInputButton);
      }
      return;
    }

    const removeButton = closest(target, "[data-remove-row]");
    if (removeButton) {
      const row = removeButton.closest(".kv-row, .input-row");
      if (
        row &&
        !row.classList.contains("kv-row-template") &&
        !row.classList.contains("input-row-template")
      ) {
        row.remove();
      }
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
  initializeAppConfigForms();
})();
