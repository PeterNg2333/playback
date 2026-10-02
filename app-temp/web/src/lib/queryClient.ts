import { notifyManager, QueryClient } from "@tanstack/react-query";

// Deliver cache changes to React in the same task as the change. An optimistic update
// then reaches a controlled input, such as the translation checkbox, before the browser
// puts the input back to its previous value.
notifyManager.setScheduler(queueMicrotask);

// Server data is refreshed by fixed polls and after each write. A failed read is shown
// once to the user and is not retried behind their back.
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
      refetchOnWindowFocus: false,
      refetchOnReconnect: false,
      refetchIntervalInBackground: true,
    },
  },
});
