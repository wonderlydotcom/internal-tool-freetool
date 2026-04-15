import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/api/api";
import ResourcesView from "@/features/space/components/ResourcesView";

vi.mock("@/api/api");
vi.mock("@/hooks/usePermissions", () => ({
  useHasPermission: () => true,
  useSpacePermissions: () => ({
    permissions: {},
    isLoading: false,
    error: null,
    refetch: vi.fn(),
    can: () => true,
  }),
}));

const mockResource = {
  id: "res-1",
  name: "My API",
  description: "A test resource",
  resourceKind: "http",
  baseUrl: "https://api.example.com",
  urlParameters: [],
  headers: [],
  body: [],
  databaseConfig: {
    databaseName: "",
    host: "",
    port: "",
    engine: "postgres",
    authScheme: "username_password",
    username: "",
    password: "",
    useSsl: false,
    enableSshTunnel: false,
    connectionOptions: [],
  },
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>
  );
}

describe("ResourcesView — delete confirmation dialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getResources).mockResolvedValue({
      data: { items: [mockResource], totalCount: 1 },
    });
    vi.mocked(api.getApps).mockResolvedValue({
      data: { items: [], totalCount: 0 },
    });
    vi.mocked(api.deleteResource).mockResolvedValue({});
  });

  it("shows the confirmation dialog when trash is clicked", async () => {
    renderWithProviders(<ResourcesView spaceId="space-1" />);

    await waitFor(() => screen.getByText("My API"));

    await userEvent.click(
      screen.getByRole("button", { name: "Delete resource" })
    );

    expect(screen.getByText("Delete resource?")).toBeInTheDocument();
    expect(
      screen.getByText("This action cannot be undone.")
    ).toBeInTheDocument();
  });

  it("does not delete when Cancel is clicked", async () => {
    renderWithProviders(<ResourcesView spaceId="space-1" />);

    await waitFor(() => screen.getByText("My API"));

    await userEvent.click(
      screen.getByRole("button", { name: "Delete resource" })
    );
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(api.deleteResource).not.toHaveBeenCalled();
    expect(screen.queryByText("Delete resource?")).not.toBeInTheDocument();
  });

  it("calls deleteResource when Delete is confirmed", async () => {
    renderWithProviders(<ResourcesView spaceId="space-1" />);

    await waitFor(() => screen.getByText("My API"));

    await userEvent.click(
      screen.getByRole("button", { name: "Delete resource" })
    );
    await userEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() => {
      expect(api.deleteResource).toHaveBeenCalledWith("res-1");
    });
  });

  it("closes the dialog when Escape is pressed", async () => {
    renderWithProviders(<ResourcesView spaceId="space-1" />);

    await waitFor(() => screen.getByText("My API"));

    await userEvent.click(
      screen.getByRole("button", { name: "Delete resource" })
    );
    expect(screen.getByText("Delete resource?")).toBeInTheDocument();

    await userEvent.keyboard("{Escape}");

    await waitFor(() =>
      expect(screen.queryByText("Delete resource?")).not.toBeInTheDocument()
    );
    expect(api.deleteResource).not.toHaveBeenCalled();
  });

  it("shows Deleting... while delete is in flight", async () => {
    let resolveDelete!: () => void;
    vi.mocked(api.deleteResource).mockReturnValue(
      new Promise((resolve) => {
        resolveDelete = () => resolve({});
      })
    );

    renderWithProviders(<ResourcesView spaceId="space-1" />);

    await waitFor(() => screen.getByText("My API"));

    await userEvent.click(
      screen.getByRole("button", { name: "Delete resource" })
    );
    await userEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Deleting..." })).toBeDisabled()
    );

    resolveDelete();
  });
});
