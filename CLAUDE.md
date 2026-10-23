# Workspace Organizer — development notes

## Line endings: CRLF everywhere

Every text file in this repo (C#, AXAML, csproj/slnx, Markdown, JSON, config files) uses **CRLF** line endings.

- New and edited files must use CRLF. Many tools write LF by default, including file-writing tools, `sed`, heredocs
  and `echo` in Git Bash, so don't assume: check after creating or rewriting files.
- `.gitattributes` (`* text=auto eol=crlf`) makes git check files out with CRLF on any OS, and `.editorconfig`
  (`end_of_line = crlf`) tells editors such as Rider to write CRLF.
- Text files the app generates at runtime (activate scripts, `.wso-profile.json`, seeded `.gitconfig`) are written with
  `Environment.NewLine` / `AppendLine`, which is CRLF on Windows. Keep it that way; don't hard-code `"\n"` in written
  files.
- Check before finishing a change. This lists working-tree files that aren't CRLF (new files need
  `git add --intent-to-add -A` first to be listed) and should print nothing:

  ```sh
  git ls-files --eol | grep -v "w/crlf\|w/-text\|w/none"
  ```

  Don't rely on `grep -c $'\r$'` in Git Bash; it miscounts. To convert a listed file: `sed -i 's/\r*$/\r/' <file>`.
