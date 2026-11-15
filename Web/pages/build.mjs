import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';

const assets = new URL('../assets/', import.meta.url);
const site = new URL('../../output/pages/', import.meta.url);
const template = await readFile(new URL('index.html', assets), 'utf8');
const script = '<script src="app.js" defer></script>';
const heading = '<main class="workspace">';
if (!template.includes(script) || !template.includes(heading)) {
  throw new Error('The DailyPlanner UI no longer matches the Pages entry points.');
}

await mkdir(site, { recursive: true });
await Promise.all([
  ...['app.js', 'style.css', 'dailyplanner-icon.png'].map(file =>
    copyFile(new URL(file, assets), new URL(file, site))),
  copyFile(new URL('browser.js', import.meta.url), new URL('browser.js', site)),
  copyFile(new URL('../../samples/tasks.json', import.meta.url), new URL('tasks.json', site)),
  writeFile(new URL('index.html', site), template
    .replace(script, '<script src="browser.js" defer></script>\n  ' + script)
    .replace(heading, heading + '\n    <p class="muted">Browser demo · Changes are saved on this device. <a href="https://github.com/Abenovv/Finale-of-ITFP#run">Run the full C# app</a></p>')),
]);
