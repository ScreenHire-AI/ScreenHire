#!/usr/bin/env node
// PreToolUse hook (Bash and PowerShell): refuses commands that can stop processes or containers this repo did not
// start. This machine also runs Kangentic, the board that drives these tasks (its dev server owns :5173 and Electron
// owns other local ports), plus other projects' containers. Killing by name, port, process tree or wildcard has
// already taken Kangentic down once.
//
// Allowed: stopping a single PID whose command line or executable path lies inside the current working tree, and
// removing a container by exact name. Everything else is denied with a reason that points at the safe route.
import { spawnSync } from 'node:child_process';

const DENY = [
  [/\btaskkill\b[^\n]*\s(\/{1,2}|-)(t|im|fi)\b/i, 'taskkill by image name, filter or process tree'],
  [/\b(pkill|killall|fuser|kill-port)\b/i, 'killing processes by name or port'],
  [/\bStop-Process\b[^\n]*-(Name|ProcessName|InputObject)\b/i, 'Stop-Process by name'],
  [/\|\s*(Stop-Process|kill)\b/i, 'piping processes into Stop-Process'],
  [/\b(Invoke-CimMethod|wmic)\b[^\n]*\b(Terminate|delete)\b/i, 'terminating processes through WMI'],
  [/\.Kill\(\)/, 'calling .Kill() on processes'],
  [/\bdocker\s+(system|container|volume|image|network|builder)\s+prune\b/i, 'pruning Docker objects'],
  [/\bdocker\b[^\n]*\s(rm|stop|kill|rmi)\b[^\n]*(\$\(|`)/i, 'removing Docker containers selected by a sub-command'],
  [/\b(shutdown(\.exe)?\s|Restart-Computer|Stop-Computer|Stop-Service|Restart-Service)\b/i, 'stopping services or the machine'],
  [/\bnet\s+stop\b/i, 'stopping services'],
  [/\bwsl(\.exe)?\s+--(shutdown|terminate)\b/i, 'stopping WSL (Docker Desktop runs on it)'],
];

const SAFE_ROUTE =
  'Stop only what you started: use TaskStop for a background command, let Aspire tests dispose their own app, or stop ' +
  'a single PID after confirming its command line points inside this working tree. Remove containers by exact name ' +
  'after checking their Aspire labels. See CLAUDE.md ("Never stop processes by name, port or process tree").';

function deny(reason) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: `${reason} is blocked on this machine. ${SAFE_ROUTE}`,
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

  for (const [pattern, label] of DENY) {
    if (pattern.test(command)) {
      deny(`${label[0].toUpperCase()}${label.slice(1)}`);
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
