// A cited passage of a teaching material is identified as
// "<material id>_p<start>_<end>_<hash>", where start and end are character offsets in the
// material (backend MaterialSources). This is the only place that reads that format;
// the API could return the parts directly instead.
const MATERIAL_PASSAGE_ID = /^([a-f0-9]{32})_p(\d+)_(\d+)_[a-f0-9]{12}$/;

export type MaterialPassage = {
  materialId: string;
  start: number;
  end: number;
};

export function parseMaterialPassageId(id: string): MaterialPassage | null {
  const match = MATERIAL_PASSAGE_ID.exec(id);
  return match
    ? { materialId: match[1], start: Number(match[2]), end: Number(match[3]) }
    : null;
}
