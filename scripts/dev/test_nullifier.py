"""Offline checks of the nullifier launcher and bench; no client is spawned or attached.
    .venv/Scripts/python.exe -m unittest discover -s scripts/dev -p test_nullifier.py
"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest
from unittest.mock import patch

import nullifier

spec = importlib.util.spec_from_file_location("nullifier_bench", Path(__file__).with_name("nullifier-bench.py"))
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)


class NullifierTests(unittest.TestCase):
    def test_settings_must_be_the_clients_own(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(SystemExit):
                nullifier.settings_name(folder)  # no config.json: the default settings file would be used
            config = Path(folder) / "config.json"
            for name in ["poeoffline_Config.ini", "Production_Config.ini", "", None]:
                config.write_text(json.dumps({"GGG_CONFIG_FILE_NAME": name}), encoding="ascii")
                with self.assertRaises(SystemExit):
                    nullifier.settings_name(folder)
            config.write_text(json.dumps({"GGG_CONFIG_FILE_NAME": "localexile-x-bot_Config.ini"}), encoding="ascii")
            self.assertEqual(nullifier.settings_name(folder), "localexile-x-bot_Config.ini")

    def test_the_shared_client_folder_is_refused(self):
        with self.assertRaises(SystemExit):
            nullifier.main(["--cwd", str(nullifier.CLIENT.parent), "--patch-port", "1", "--login-port", "2"])

    def test_record_merges_atomically(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "s.json"
            path.write_text(json.dumps({"pid": 0, "started": 1, "session": "s", "folder": "f"}), encoding="ascii")
            nullifier.write_record(path, {"pid": 7, "started": 8, "launcher": 9, "launcherStarted": 10})
            self.assertEqual(json.loads(path.read_text()), {"pid": 7, "started": 8, "session": "s", "folder": "f",
                                                            "launcher": 9, "launcherStarted": 10})
            self.assertEqual([p.name for p in Path(folder).iterdir()], ["s.json"])

    def test_record_replace_is_retried_while_a_reader_holds_it(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "s.json"
            path.write_text("{}", encoding="ascii")
            real = os.replace
            calls = []

            def flaky(source, target):
                calls.append(source)
                if len(calls) < 3:
                    raise PermissionError(5, "Access is denied")
                real(source, target)
            with patch.object(nullifier.os, "replace", side_effect=flaky):
                nullifier.write_record(path, {"pid": 7}, pause=0)
            self.assertEqual(len(calls), 3)
            self.assertEqual(json.loads(path.read_text()), {"pid": 7})
            with patch.object(nullifier.os, "replace", side_effect=PermissionError(5, "Access is denied")):
                with self.assertRaises(PermissionError):
                    nullifier.write_record(path, {"pid": 8}, attempts=3, pause=0)
            self.assertEqual([p.name for p in Path(folder).iterdir()], ["s.json"])  # no temporary file left

    def test_a_failed_child_setup_terminates_the_suspended_client(self):
        calls = []
        fake = type("Kernel", (), {})()
        fake.OpenProcess = lambda *a: 1234
        fake.CreateJobObjectW = lambda *a: 0  # fails right after the handle was opened
        fake.WaitForSingleObject = lambda *a: nullifier.WAIT_TIMEOUT
        fake.TerminateProcess = lambda handle, code: calls.append(("terminate", handle))
        fake.CloseHandle = lambda handle: calls.append(("close", handle))
        with patch.object(nullifier, "kernel", fake):
            with self.assertRaises(OSError):
                nullifier.Child(42)
        self.assertEqual(calls, [("terminate", 1234), ("close", 1234)])

    def launch(self, messages, alive=False):
        """main() with Frida, the process handle and the settings check mocked; `messages` reach the agent's
        message handler when the script loads."""
        events = []

        class Script:
            def on(self, name, handler): self.handler = handler
            def load(self):
                for text in messages:
                    self.handler({"type": "send", "payload": {"type": "log", "message": text}}, None)

        class Session:
            def on(self, *a): pass
            def create_script(self, *a, **k): return Script()
            def detach(self): events.append("detach")

        class Device:
            def spawn(self, argv, **k): events.append(("spawn", k["cwd"], k["stdio"])); return 4321
            def attach(self, pid): return Session()
            def resume(self, pid): events.append("resume")
            def kill(self, pid): events.append("kill")

        class FakeChild:
            started = 99
            def __init__(self, pid): self.pid = pid
            def limit(self, *a): events.append("limit")
            def alive(self): return alive
            def trim(self): pass
            def close(self): events.append("close")

        with tempfile.TemporaryDirectory() as folder,                 patch.object(nullifier.frida, "get_local_device", return_value=Device()),                 patch.object(nullifier, "Child", FakeChild),                 patch.object(nullifier, "settings_name", return_value="localexile-x-s_Config.ini"),                 patch.object(nullifier, "MUTEX_CONFIRMATION_SECONDS", 0.05),                 patch.object(nullifier.client_control, "process_info", return_value=("python", 5)),                 contextlib.redirect_stdout(io.StringIO()):
            record = Path(folder) / "s.json"
            record.write_text(json.dumps({"pid": 0, "started": 1, "session": "s"}), encoding="ascii")
            try:
                nullifier.main(["--cwd", folder, "--record", str(record), "--patch-port", "1", "--login-port", "2"])
                outcome = "ok"
            except SystemExit as stop:
                outcome = str(stop)
            return outcome, events, json.loads(record.read_text())

    def test_launch_waits_for_the_run_mutex_rename(self):
        outcome, events, record = self.launch(["engine options: render=0", "run mutex -> Local\\X-PoERunMutexA"])
        self.assertEqual(outcome, "ok")
        self.assertEqual(events[0][2], "pipe")
        self.assertEqual(events[1:3], ["limit", "resume"])
        self.assertEqual((record["pid"], record["started"], record["launcherStarted"], record["session"]), (4321, 99, 5, "s"))
        self.assertEqual(events[-2:], ["close", "detach"])

    def test_launch_without_the_rename_closes_the_client_and_records_nothing(self):
        outcome, events, record = self.launch(["engine options: render=0"], alive=True)
        self.assertIn("no run-mutex rename", outcome)
        self.assertIn("close", events)  # the client goes (the job and the handle)
        self.assertEqual(record["pid"], 0)  # live.ps1 sees no client and fails the start

    def test_run_mutex_prefix_is_session_local_and_unique(self):
        a = nullifier.run_mutex_prefix(Path("data/clients/bot1"))
        b = nullifier.run_mutex_prefix(Path("data/clients/bot2"))
        self.assertTrue(a.startswith("Local\\LocalExile-") and a.endswith("-"), a)
        self.assertNotIn("Global", a)
        self.assertNotEqual(a, b)
        self.assertNotIn("\\", a[len("Local\\"):])  # one namespace separator only
        self.assertRegex(nullifier.run_mutex_prefix(Path("x y/a b")), r"^Local\\LocalExile-[A-Za-z0-9_-]+-$")

    def test_options_and_client_command(self):
        with patch.object(nullifier.client_control, "stack_port", side_effect=lambda name: {"patch": 21000, "login": 21002}[name]):
            options = nullifier.parse(["--cwd", "data/clients/s1"])
        self.assertEqual((options.patch_port, options.login_port, options.renderer), (21000, 21002, "configured"))
        self.assertEqual(nullifier.client_argv(options),
                         [str(nullifier.CLIENT), "--patching-server", "127.0.0.1:21000", "--nologo", "--nosound"])
        agent = nullifier.agent_options(options, Path("data/clients/s1"))
        self.assertFalse(agent["nullRenderer"])
        self.assertEqual(agent["runMutexPrefix"], nullifier.run_mutex_prefix(Path("data/clients/s1")))
        self.assertIsNone(agent["crashLog"])
        options = nullifier.parse(["--cwd", "x", "--patch-port", "1", "--login-port", "2", "--renderer", "null",
                                   "--trim-seconds", "10", "--sound"])
        self.assertTrue(nullifier.agent_options(options, Path("x"))["nullRenderer"])
        self.assertEqual(nullifier.client_argv(options)[-1], "--nologo")

    def test_preset_and_agent_exist(self):
        text = (nullifier.ROOT / "config" / "nullifier_Config.ini").read_text(encoding="utf-8")
        self.assertIn("[DISPLAY]", text)
        self.assertNotIn("password", text.lower())
        self.assertNotIn("[LOGIN]", text)  # no saved login for whichever login server the gateway list names

    @unittest.skipUnless(shutil.which("node"), "node not installed")
    def test_agent_renames_only_the_two_copies_mutexes(self):
        result = subprocess.run(["node", str(Path(__file__).with_name("test-nullifier-mutex.cjs"))], text=True,
                                capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)

    @unittest.skipUnless(shutil.which("node"), "node not installed")
    def test_agent_parses_with_options(self):
        with tempfile.TemporaryDirectory() as folder:
            agent = Path(folder) / "nullifier.js"
            agent.write_text("const OPTIONS = {};\n" + nullifier.AGENT.read_text(encoding="utf-8"), encoding="utf-8")
            result = subprocess.run(["node", "--check", str(agent)], text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)


class WalkmapsRecordTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location(
            "walkmaps", Path(__file__).resolve().parents[1] / "walkmaps" / "walkmaps.py")
        self.walkmaps = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.walkmaps)

    def test_only_the_recorded_live_client_is_attached_to(self):
        client = str(nullifier.CLIENT)
        with tempfile.TemporaryDirectory() as folder:
            record = Path(folder) / "default.json"
            record.write_text(json.dumps({"pid": 50, "started": 1_000_000_000}), encoding="ascii")
            recorded = self.walkmaps.recorded_pid
            self.assertEqual(recorded(str(record), info=lambda pid: (client, 1_000_000_000 + 5)), 50)
            self.assertIsNone(recorded(str(record), info=lambda pid: None))  # gone
            self.assertIsNone(recorded(str(record), info=lambda pid: (client, 9_000_000_000)))  # a reused pid
            self.assertIsNone(recorded(str(record), info=lambda pid: ("C:/Other/Client/Game.patched.exe", 1_000_000_000)))
            record.write_text(json.dumps({"pid": 0, "started": 1}), encoding="ascii")
            self.assertIsNone(recorded(str(record), info=lambda pid: (client, 1)))  # a pending launch


class BenchTests(unittest.TestCase):
    def test_lock_is_exclusive_and_foreign_formats_are_held(self):
        with tempfile.TemporaryDirectory() as folder:
            lock = Path(folder) / ".extra-client.lock"
            lock.write_text("pr-campaign-a 2026-10-30T21:22:49\n", encoding="utf-8")  # another format: held
            with self.assertRaises(SystemExit):
                bench.take_lock(lock)
            self.assertIn("pr-campaign-a", lock.read_text())
            lock.write_text(f"nullifier-bench {os.getpid()} now\n", encoding="utf-8")  # a live run of ours: held
            with self.assertRaises(SystemExit):
                bench.take_lock(lock)
            lock.write_text("nullifier-bench 4294967291 then\n", encoding="utf-8")  # a dead run of ours: taken over
            with patch.object(bench, "alive_pid", return_value=False):
                bench.take_lock(lock)
            self.assertEqual(lock.read_text().split()[:2], ["nullifier-bench", str(os.getpid())])
            bench.release_lock(lock)
            self.assertFalse(lock.exists())

    def test_release_compares_whole_tokens(self):
        with tempfile.TemporaryDirectory() as folder:
            lock = Path(folder) / "l"
            lock.write_text(f"nullifier-bench {os.getpid()}1 x\n", encoding="utf-8")
            bench.release_lock(lock)
            self.assertTrue(lock.exists())

    def test_bridge_calls_time_out(self):
        self.assertEqual(bench.with_timeout(1, lambda x: x + 1, 1), 2)
        with self.assertRaises(TimeoutError):
            bench.with_timeout(0.05, time.sleep, 1)
        with self.assertRaises(KeyError):
            bench.with_timeout(1, {}.__getitem__, "x")

    def test_client_handle_must_be_the_recorded_process(self):
        with patch.object(bench.client_control, "process_info", return_value=("x", 1_000_000_000)):
            with self.assertRaises(RuntimeError):
                bench.Client(os.getpid(), 5_000_000_000)  # another start time: not the recorded client
        own = bench.client_control.process_info(os.getpid())
        client = bench.Client(os.getpid(), own[1])
        self.assertTrue(client.alive())
        client.close()
        self.assertFalse(client.alive())


if __name__ == "__main__":
    unittest.main()
