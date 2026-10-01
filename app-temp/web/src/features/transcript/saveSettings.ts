import { queryClient } from "../../lib/queryClient";
import { api } from "../../lib/backend/client";
import { runAction } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { sessionQuery } from "../library/libraryQueries";
import { refreshWorkspace } from "../library/refreshWorkspace";

// Per-session recognition and note languages, and transcript translation.

export function saveLanguages(
  session: Session,
  asrLanguage: Session["asrLanguage"],
  noteLanguage: Session["noteLanguage"],
  asrModel: string | null = session.asrModel ?? null,
) {
  return runAction("languages", async () => {
    await api.put(`/sessions/${session.id}/languages`, {
      asrLanguage,
      noteLanguage,
      asrModel,
    });
    await refreshWorkspace(session.id);
  });
}

// Shows the new translation setting at once and puts the old one back if saving fails.
export function saveTranslation(
  session: Session,
  enabled: boolean,
  language = session.translationLanguage || "zh-Hant",
) {
  const key = sessionQuery(session.id).queryKey;
  void queryClient.cancelQueries({ queryKey: key });
  queryClient.setQueryData(key, {
    ...session,
    translationEnabled: enabled,
    translationLanguage: language,
  });
  return runAction("translation", async () => {
    try {
      await api.put(`/sessions/${session.id}/translation`, {
        enabled,
        language,
      });
      await refreshWorkspace(session.id);
    } catch (error) {
      queryClient.setQueryData(key, session);
      throw error;
    }
  });
}

export function retryTranslations(session: Session) {
  return runAction("translation", async () => {
    await api.post(`/sessions/${session.id}/translation/retry`);
    await refreshWorkspace(session.id);
  });
}
