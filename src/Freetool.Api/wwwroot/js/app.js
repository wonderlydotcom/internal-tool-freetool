(() => {
  function closest(target, selector) {
    return target instanceof Element ? target.closest(selector) : null;
  }

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
})();
