// A module imported with "?url" is bundled as a file, and the import is its URL.
declare module "*?url" {
  const url: string;
  export default url;
}
