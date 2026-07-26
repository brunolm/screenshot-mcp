# screenshot-mcp

An MCP server that lets Claude see your screen. It captures your monitors — or one monitor, one
window, or one rectangle — writes the image to the temp folder, and returns both the path and the
image itself, so you can just ask a question about what you are looking at.

Windows only. Written in C# on .NET 9.

## Why it is fast

- **GDI `BitBlt`** for screens. For one-shot grabs this beats DXGI Desktop Duplication, whose setup
  cost only amortizes when you capture continuously.
- **Downscales to 1568px on the long edge** by default. That is the size Claude reduces images to
  internally, so anything larger costs encode and transfer time without adding readable detail.
- **Returns the image inline**, so no second round-trip to read the file off disk.

A three-monitor capture takes roughly 300–500 ms end to end; a single window about 150 ms.

## Tools

### `capture_screen`

Screenshots your monitors. With no arguments it captures every monitor as its own image, which stays
far more readable than one stitched panorama.

| Argument | Default | Notes |
|---|---|---|
| `monitor` | `"all"` | `"all"`, `"primary"`, or a 0-based index like `"1"` |
| `combined` | `false` | Stitch all monitors into one wide image instead of one image each |
| `region` | – | `"x,y,width,height"` crop, relative to the captured area's top-left |
| `maxLongEdge` | `1568` | Longest output edge in pixels; `0` keeps native resolution |
| `format` | `"png"` | `"png"` for sharper text, `"jpeg"` for a smaller file |
| `includeCursor` | `false` | Draw the mouse pointer into the capture |
| `returnImage` | `true` | Set `false` to get only the file path |

### `capture_window`

Screenshots a single window, **even when another window is covering it**, via `PrintWindow` with
`PW_RENDERFULLCONTENT`. Windows that refuse to render themselves fall back to a screen-region grab.

`window` accepts part of a title, a process name, or a handle. Matches are ranked most-specific
first, so `"Steam"` picks the Steam client rather than a browser tab that happens to mention Steam;
any other matches come back in `otherMatches` so you can retry with a handle.

The rest of the arguments match `capture_screen`.

### `list_capture_targets`

Lists monitors (index, device, bounds, DPI scale) and visible top-level windows (title, process,
class, handle, bounds). Use it to pick a target before capturing.

## Install

```powershell
git clone https://github.com/brunolm/screenshot-mcp.git
cd screenshot-mcp
dotnet publish src/ScreenshotMcp -c Release -o publish
```

Then register the published executable:

```powershell
claude mcp add screenshot --scope user -- "<repo>\publish\screenshot-mcp.exe"
```

Or in an MCP client config file:

```json
{
  "mcpServers": {
    "screenshot": {
      "command": "C:\\path\\to\\screenshot-mcp\\publish\\screenshot-mcp.exe"
    }
  }
}
```

## Output

Images land in `%TEMP%\screenshot-mcp\`, named by timestamp and target. Anything older than six
hours is deleted at startup, so the folder cannot grow without bound.

## Notes

- The process declares itself per-monitor DPI aware, so a scaled 4K display is captured at its true
  3840×2160 rather than a blurry virtualised size.
- Layered and transparent windows are included via `CAPTUREBLT`.
- A minimized window has nothing to capture; the tool says so instead of returning a blank image.
- Screenshots can contain anything on screen, including credentials and private messages. Capturing
  a single window rather than everything is the safer habit.
