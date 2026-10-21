#!/usr/bin/env node
// PreToolUse hook (Bash and PowerShell): refuses commands that can stop processes or containers this repo did not
// start. This machine also runs Kangentic, the board that drives these tasks (its dev server owns :5173 and Electron
// owns other local ports), plus other projects' containers. Killing by name, port, process tree or wildcard has
// already taken Kangentic down once.
//
// Allowed: stopping a single PID whose command line or executable path lies inside the current working tree, and
// removing a container by exact name. Everything else is denied with a reason that points at the safe route.
//
// It also refuses launching Docker Desktop directly. Started from a task's shell, Docker inherits the task's
// KANGENTIC_TASK_ID and the worktree as its working directory, so Kangentic's Done cleanup keeps it and cannot delete
// the worktree. `pnpm docker:start` (scripts/start-docker.mjs) starts it without either. Read-only commands that name
// it (Test-Path, Get-Process) still run.
import { spawnSync } from 'node:child_process';

const SAFE_ROUTE =
  'Stop only what you started: use TaskStop for a background command (except the gate, `pnpm verify`: let it finish, ' +
  'see /verify), let Aspire tests dispose their own app, or stop a single PID after confirming its command line ' +
  'points inside this working tree. Remove containers by exact name after checking their Aspire labels. See ' +
  'CLAUDE.md ("Never stop processes by name, port or process tree").';

const DOCKER_ROUTE =
  'Run `pnpm docker:start`: it starts Docker Desktop outside this task (no task tag, Docker\'s own folder as working ' +
  'directory) and waits for the engine. If it fails, report Docker as infrastructure.';

// A path to Docker Desktop's executable: quoted, or with escaped spaces as Git Bash writes it.
const DOCKER_EXE = String.raw`(?:['"][^'"\n]*Docker Desktop\.exe['"]|(?:[^\s'";|&]|\\ )*Docker\\ Desktop\.exe)`;

const DENY = [
  [/\btaskkill\b[^\n]*\s(\/{1,2}|-)(t|im|fi)\b/i, 'taskkill by image name, filter or process tree'],
  [/\b(pkill|killall|fuser|kill-port)\b/i, 'killing processes by name or port'],
  [/\bStop-Process\b[^\n]*-(Name|ProcessName|InputObject)\b/i, 'Stop-Process by name'],
  // A grep alternation (`\|kill`, `\|stop-process-tree`) is not a pipe: skip a hyphenated word, and in Bash an escaped
  // bar. PowerShell has no backslash escape, so there `x \| Stop-Process` is still a pipe.
  [/(?<!\\)\|\s*(Stop-Process|kill)(?![\w-])/i, 'piping processes into Stop-Process'],
  [/\|\s*(Stop-Process|kill)(?![\w-])/i, 'piping processes into Stop-Process', SAFE_ROUTE, 'PowerShell'],
  [/\b(Invoke-CimMethod|wmic)\b[^\n]*\b(Terminate|delete)\b/i, 'terminating processes through WMI'],
  [/\.Kill\(\)/, 'calling .Kill() on processes'],
  [/\bdocker\s+(system|container|volume|image|network|builder)\s+prune\b/i, 'pruning Docker objects'],
  [/\bdocker\b[^\n]*\s(rm|stop|kill|rmi)\b[^\n]*(\$\(|`)/i, 'removing Docker containers selected by a sub-command'],
  [/\b(shutdown(\.exe)?\s|Restart-Computer|Stop-Computer|Stop-Service|Restart-Service)\b/i, 'stopping services or the machine'],
  [/\bnet\s+stop\b/i, 'stopping services'],
  [/\bwsl(\.exe)?\s+--(shutdown|terminate)\b/i, 'stopping WSL (Docker Desktop runs on it)'],
  // Launching Docker Desktop. Each entry is one launch form; naming it in a read-only command matches none of them.
  // Within the one command: `Start-Process notepad; Get-Process 'Docker Desktop'` only reads.
  [/\b(Start-Process|saps)\b[^;|&\n]*Docker[ _-]?Desktop/i, 'starting Docker Desktop with Start-Process', DOCKER_ROUTE],
  [
    new RegExp(String.raw`(^|[;|&(]|\n)\s*(?:[&.]\s*)?${DOCKER_EXE}`, 'i'),
    'running Docker Desktop.exe directly',
    DOCKER_ROUTE,
  ],
  [
    // The path is the launcher's argument, after any switches (`/c`, `-Path`) and `start`'s empty title.
    new RegExp(String.raw`(?<![\w-])(?:start|Invoke-Item|ii|explorer(?:\.exe)?|cmd(?:\.exe)?)(?:\s+(?:[/-]+\w+|""|''))*\s+${DOCKER_EXE}`, 'i'),
    'opening Docker Desktop.exe',
    DOCKER_ROUTE,
  ],
  [/\bdocker(\.exe)?\s+desktop\s+(start|restart)\b/i, 'docker desktop start or restart', DOCKER_ROUTE],
];

function deny(reason, route = SAFE_ROUTE) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: `${reason} is blocked on this machine. ${route}`,
      },
    }),
  );
}

function normalize(path) {
  return path.replaceAll('\\', '/').replace(/\/+$/, '').toLowerCase();
}

// Returns the PIDs a single-PID stop targets, or null when the command is not one.
function targetedPids(command) {
  const patterns = [
    /\btaskkill\b[^\n]*?[/-]pid\s+(\d+)/gi,
    /\bStop-Process\b[^\n]*?-Id\s+(\d+)/gi,
    /(?:^|[;&|]\s*)kill\s+(?:-\w+\s+)*(\d+)/gi,
  ];
  const pids = [];
  for (const pattern of patterns) {
    for (const match of command.matchAll(pattern)) {
      pids.push(match[1]);
    }
  }
  return pids.length > 0 ? pids : null;
}

function processPaths(pid) {
  if (process.platform !== 'win32') {
    const result = spawnSync('ps', ['-o', 'args=', '-p', pid], { encoding: 'utf8' });
    return result.stdout.trim();
  }
  const script = `$p = Get-CimInstance Win32_Process -Filter 'ProcessId=${pid}'; if ($p) { $p.CommandLine; $p.ExecutablePath }`;
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8',
    timeout: 15000,
  });
  return (result.stdout ?? '').trim();
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  input += chunk;
});
process.stdin.on('end', () => {
  let data;
  try {
    data = JSON.parse(input);
  } catch {
    return;
  }
  if (data.tool_name !== 'Bash' && data.tool_name !== 'PowerShell') return;
  const command = data.tool_input?.command;
  if (typeof command !== 'string') return;

  for (const [pattern, label, route, tool] of DENY) {
    if ((!tool || tool === data.tool_name) && pattern.test(command)) {
      deny(`${label[0].toUpperCase()}${label.slice(1)}`, route);
      return;
    }
  }

  const pids = targetedPids(command);
  if (!pids) return;
  const cwd = data.cwd ?? process.cwd();
  const toplevel = spawnSync('git', ['-C', cwd, 'rev-parse', '--show-toplevel'], { encoding: 'utf8' }).stdout?.trim();
  const tree = normalize(toplevel || cwd);
  for (const pid of pids) {
    const paths = normalize(processPaths(pid));
    if (!paths) {
      deny(`Stopping PID ${pid} (no such process, or its command line could not be read)`);
      return;
    }
    if (!paths.includes(tree)) {
      deny(`Stopping PID ${pid}, which was not started from ${toplevel || cwd}`);
      return;
    }
  }
});
