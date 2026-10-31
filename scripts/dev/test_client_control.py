"""Pure protocol/transport checks; no client attach, login, or saves are touched."""
import contextlib
import io
from itertools import chain, repeat
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock, call, patch

from client_control import (
    ClientControl, chat_packet, collect_replies, connection_ports, find_client, flask_packet, launched_path, log_mark,
    normalize, process_info, read_records, read_replies, run_request, same_path, session_name, stack_port, tp_text,
    waypoint_packet,
)


class ClientControlTests(unittest.TestCase):
    def test_command_alias_keeps_character_name_intact(self):
        self.assertEqual(normalize("character:play:OfflineWitch"),
                         {"command": "character:play", "name": "OfflineWitch"})
        source = {"command": "login:offline", "password": "test-only"}
        self.assertEqual(normalize(source)["account"], "offline")
        self.assertNotIn("account", source)

    def test_chat_matches_client_compact_wide_string_encoding(self):
        self.assertEqual(bytes(chat_packet("/hit physical 1")), b"\x00\x15\x00\x0f/hit physical 1\x00")
        self.assertEqual(bytes(chat_packet("/\u00e9\U0001f600")),
                         b"\x00\x15\x00\x03/\x80\xe9\x00\xc0\x00\xf6\x01\x00\x00")

    def test_rejects_invalid_request_and_oversized_text(self):
        for value in [None, [], 3, {"command": 7}]:
            with self.assertRaises(ValueError):
                normalize(value)
        for value in ["", "a" * 1025, None]:
            with self.assertRaises(ValueError):
                chat_packet(value)

    def test_result_does_not_echo_password_or_request(self):
        class Reject:
            def execute(self, request):
                raise RuntimeError("Local login rejected (result 6)")
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = run_request(Reject(), {"command": "login", "password": "do-not-echo"})
        self.assertFalse(result["ok"])
        self.assertNotIn("do-not-echo", json.dumps(result))
        self.assertEqual("", output.getvalue())

    def test_tp_requests_become_tp_commands(self):
        self.assertEqual(tp_text({"x": 120, "y": 340}), "/tp 120 340")
        self.assertEqual(tp_text({"dx": 5, "dy": -3}), "/tp +5 -3")
        self.assertEqual(tp_text({"id": 0x1234}), "/tp 0x1234")
        self.assertEqual(tp_text({"target": "RhoaChest", "index": 2}), "/tp RhoaChest 2")
        self.assertEqual(tp_text({"text": "GlyphWall"}), "/tp GlyphWall")
        self.assertEqual(tp_text({"list": "chest"}), "/tp list chest")
        self.assertEqual(tp_text({"list": True}), "/tp list")
        for bad in [{}, {"x": 1}, {"x": "1", "y": 2}, {"x": True, "y": 2}, {"target": " "}]:
            with self.assertRaises((ValueError, KeyError)):
                tp_text(bad)

    def test_flask_use_sends_the_flask_key_message(self):
        self.assertEqual(flask_packet({"key": 1}), ([0x00, 0x70, 0, 0, 0, 0], 0))
        self.assertEqual(flask_packet({"slot": 4}), ([0x00, 0x70, 0, 0, 0, 4], 4))
        self.assertEqual(flask_packet({"key": 5})[1], 4)
        for bad in [{}, {"slot": 1, "key": 2}, {"slot": 5}, {"key": 0}, {"key": "1"}, {"slot": True}]:
            with self.assertRaises(ValueError):
                flask_packet(bad)

    def test_waypoint_travel_is_the_pin_message(self):
        self.assertEqual(waypoint_packet({"area": 0x7ad2}), ([0x00, 0x4d, 0x7a, 0xd2, 0, 0, 0x2f, 0x00, 0], 0x7ad2))
        self.assertEqual(waypoint_packet({"area": "0x235c"})[1], 0x235c)
        for bad in [{}, {"area": 0}, {"area": 0x10000}, {"area": "x"}, {"area": True}, {"area": 1.5}]:
            with self.assertRaises(ValueError):
                waypoint_packet(bad)

    def test_waypoint_travel_and_frames_go_through_the_bridge(self):
        control = ClientControl.__new__(ClientControl)
        control.rpc = Mock(return_value={"submitted": True})
        self.assertEqual(control.execute({"command": "waypoint:travel", "area": "0x7ad2"}),
                         {"submitted": True, "area": 0x7ad2})
        control.rpc.assert_called_once_with("interaction", bytes=[0x00, 0x4d, 0x7a, 0xd2, 0, 0, 0x2f, 0x00, 0])
        control.attach = Mock()
        control.pid = 7
        control.script = Mock()
        control.script.exports_sync.frames.return_value = {"frames": 92, "seconds": 10.0, "fpsSinceLastCall": 9.2}
        self.assertEqual(control.execute({"command": "game:frames"}),
                         {"pid": 7, "frames": 92, "seconds": 10.0, "fpsSinceLastCall": 9.2})
        control.attach.assert_called_once()

    def test_open_outwaits_live_ps1s_session_start(self):
        # live.ps1 -ClientOnly may take the gateway-list read and write, the Python check and up to 60 s for a session
        # launcher: open() must not give up first.
        control = ClientControl.__new__(ClientControl)
        control.client_session = "s1"
        control._find_client = Mock(return_value=None)
        control.ready = Mock(return_value={"screen": "login"})
        with tempfile.TemporaryDirectory() as folder, patch("client_control.ROOT", Path(folder)),                 patch("client_control.subprocess.run") as run:
            control.open()
        self.assertGreaterEqual(run.call_args.kwargs["timeout"], 180)
        self.assertEqual(run.call_args.args[0][-3:], ["-ClientOnly", "-Session", "s1"])

    def test_bridge_script_allows_the_travel_message_and_counts_frames(self):
        source = Path(__file__).with_name("client-control.js").read_text(encoding="utf-8")
        self.assertIn("0x4d:9", source)
        self.assertIn("frameCount.frames++", source)
        self.assertIn("rpc.exports.frames", source)

    def test_replies_are_the_gateway_chat_lines_after_the_mark(self):
        nl = chr(10)
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder) / "live-gateway.log"
            log.write_text('[gateway] 127.0.0.1:1 => 0x0017 chat line "old": 0017' + nl, encoding="utf-8")
            start = log_mark(log)
            with log.open("a", encoding="utf-8", newline="") as out:
                out.write('[gateway] 127.0.0.1:1 <= 0x0015 chat "/pos"' + nl)
                out.write('[gateway] 127.0.0.1:1 => 0x0017 chat line "(10, 20) in 1_1_3": 00170000' + nl)
                out.write('[gateway] 127.0.0.1:1 => 0x0017 chat line "a "quoted": line": 0017' + nl)
                out.write('[gateway] 127.0.0.1:1 => 0x0017 chat line "partial')
            lines, offset = read_replies(log, start)
            self.assertEqual(lines, ["(10, 20) in 1_1_3", 'a "quoted": line'])
            self.assertLess(offset, log.stat().st_size)  # the unfinished line is left for the next read
            self.assertEqual(collect_replies(start, wait=0.3, quiet=0.05, path=log), lines)
            self.assertIsNone(collect_replies(None))
            self.assertIsNone(log_mark(Path(folder) / "missing.log"))

    def test_replies_of_one_client_when_several_share_the_gateway(self):
        nl = chr(10)
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder) / "live-gateway.log"
            log.write_text(
                '21:04:59.471 [gateway] 127.0.0.1:50001 => 0x0017 chat line "mine": 0017' + nl +
                '21:04:59.472 [gateway] 127.0.0.1:50002 => 0x0017 chat line "theirs": 0017' + nl +
                '21:04:59.473 [gateway] 127.0.0.1:50003 => 0x0017 chat line "mine after an area change": 0017' + nl,
                encoding="utf-8")
            self.assertEqual(read_replies(log, 0, {50001, 50003})[0], ["mine", "mine after an area change"])
            # The table could not be read: every reply, as with one client. The client has no connection to the
            # Gateway (between areas): none of them, never another client's.
            self.assertEqual(len(read_replies(log, 0, None)[0]), 3)
            self.assertEqual(read_replies(log, 0, set())[0], [])
            self.assertEqual(collect_replies(0, wait=0.2, quiet=0.05, path=log, ports={50002}), ["theirs"])


class SeveralClientsTests(unittest.TestCase):
    """Several stacks and clients on one PC: ports, sessions and which client is this checkout's."""

    def test_session_names(self):
        self.assertEqual(session_name(None), "default")
        self.assertEqual(session_name(""), "default")
        self.assertEqual(session_name("default"), "default")
        self.assertEqual(session_name("a1"), "a1")
        self.assertEqual(session_name("Agent_2-b"), "Agent_2-b")
        for bad in ["-a", "a b", "../x", "a" * 25, "a\\b", "a.json"]:
            with self.assertRaises(ValueError):
                session_name(bad)

    def test_ports_come_from_the_stack(self):
        with tempfile.TemporaryDirectory() as folder:
            stack = Path(folder) / "stack.json"
            # No setting: today's ports.
            self.assertEqual(stack_port("login", {}, stack), 20493)
            self.assertEqual(stack_port("gateway", {}, stack), 6112)
            # live.ps1 -PortBase 21000 remembered the stack's ports (written by PowerShell: a BOM is tolerated).
            stack.write_text("\ufeff" + json.dumps({"portBase": 21000, "ports": {"login": 21002, "gateway": 21004}}),
                             encoding="utf-8")
            self.assertEqual(stack_port("login", {}, stack), 21002)
            self.assertEqual(stack_port("gateway", {}, stack), 21004)
            # live.ps1 wrote the ports its servers got: they win over a shell's leftover variables.
            self.assertEqual(stack_port("login", {"Game_LOGIN_PORT": "30000", "Game_PORT_BASE": "23000"}, stack), 21002)
            # On the default ports (no file) the server's own variable applies, as for live.ps1.
            stack.unlink()
            self.assertEqual(stack_port("login", {"Game_LOGIN_PORT": "30000"}, stack), 30000)
            stack.write_text("{broken", encoding="utf-8")
            self.assertEqual(stack_port("gateway", {}, stack), 6112)

    def test_client_records(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            (folder / "a1.json").write_text("\ufeff" + json.dumps({"pid": 7, "started": 123, "session": "a1"}), encoding="utf-8")
            (folder / "broken.json").write_text("{", encoding="utf-8")
            (folder / "nopid.json").write_text("{}", encoding="utf-8")
            self.assertEqual(read_records(folder), {"a1": {"pid": 7, "started": 123}})
            self.assertEqual(read_records(folder / "missing"), {})

    def test_only_this_checkouts_client_of_the_session(self):
        mine, other = r"D:\x\pr-a\Client\Game.patched.exe", r"D:\x\pr-b\Client\Game.patched.exe"
        second = 10_000_000
        processes = {1: (other, 5 * second), 2: (mine, 10 * second), 3: (mine, 20 * second), 4: (mine, 30 * second)}
        info = processes.get
        find = lambda session, records, pids=processes: find_client(session, list(pids), info, records, mine)
        records = {"a1": {"pid": 3, "started": 20 * second + 5}, "a2": {"pid": 4, "started": 30 * second}}
        self.assertEqual(find("a1", records), 3)
        self.assertEqual(find("a2", records), 4)
        # The default session: the client no named session recorded, never another checkout's (pid 1).
        self.assertEqual(find("default", records), 2)
        self.assertEqual(find("default", {**records, "default": {"pid": 2, "started": 10 * second}}), 2)
        # A named session without a record has no client; a record whose PID now belongs to a later process neither.
        self.assertIsNone(find("b1", records))
        self.assertIsNone(find("a1", {"a1": {"pid": 3, "started": 99 * second}}, {1: None, 3: None}))
        # A recorded client that has exited (process_info gives None).
        self.assertIsNone(find("a2", records, {1: None, 2: None, 3: None}))
        # Two unrecorded clients of this checkout: ambiguous for the default session.
        with self.assertRaisesRegex(RuntimeError, "--session"):
            find("default", {})
        # Only another checkout's client runs: none of ours.
        self.assertIsNone(find("default", {}, {1: None}))
        # A launch under way (pid 0 until live.ps1 knows it): a client started after it is not the default session's.
        launching = {"a3": {"pid": 0, "started": 25 * second}}
        self.assertEqual(find_client("default", [2, 4], info, launching, mine, now=26 * second), 2)
        self.assertIsNone(find_client("default", [4], info, launching, mine, now=26 * second))
        # A pending record left behind for over a minute no longer hides anything.
        self.assertEqual(find_client("default", [4], info, launching, mine, now=99 * second), 4)

    def test_launched_path(self):
        self.assertEqual(launched_path(r'"D:\x y\Client\Game.patched.exe" --patching-server 127.0.0.1:21000'),
                         r"D:\x y\Client\Game.patched.exe")
        self.assertEqual(launched_path(r"D:\x\Client\Game.patched.exe --nologo"), r"D:\x\Client\Game.patched.exe")
        self.assertEqual(launched_path(""), "")

    @unittest.skipUnless(os.name == "nt", "Windows junctions")
    def test_a_client_started_through_a_junction_is_known_by_that_path(self):
        """Each checkout's Client is a junction to one shared folder: only the path a process was started by tells
        checkouts apart (Windows' image name resolves the junction). A real process started through a real junction."""
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / "shared"
            target.mkdir()
            exe = target / "Game.patched.exe"
            shutil.copyfile(os.path.join(os.environ["SystemRoot"], "System32", "PING.EXE"), exe)
            junction = Path(folder) / "checkout-a"
            subprocess.run(["cmd", "/c", "mklink", "/J", str(junction), str(target)], check=True, capture_output=True)
            launched = junction / "Game.patched.exe"
            child = subprocess.Popen([str(launched), "-n", "30", "127.0.0.1"], stdin=subprocess.DEVNULL,
                                     stdout=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                time.sleep(0.3)
                details = process_info(child.pid)
                self.assertIsNotNone(details)
                self.assertTrue(same_path(details[0], launched), details[0])
                self.assertEqual(find_client("default", [child.pid], records={}, client=launched), child.pid)
                # The same file through another checkout's path (here: the junction's target) is not this one's.
                self.assertIsNone(find_client("default", [child.pid], records={}, client=exe))
                record = {"a1": {"pid": child.pid, "started": details[1]}}
                self.assertEqual(find_client("a1", [child.pid], records=record, client=launched), child.pid)
                self.assertIsNone(find_client("a1", [child.pid], records=record, client=exe))
            finally:
                child.kill()
                child.wait()
                subprocess.run(["cmd", "/c", "rmdir", str(junction)], check=True, capture_output=True)

    def test_connection_ports_of_the_client(self):
        def row(state, local, remote, pid):
            swap = lambda port: ((port & 0xff) << 8) | (port >> 8)
            return struct.pack("<6I", state, 0x0100007f, swap(local), 0x0100007f, swap(remote), pid)
        table = struct.pack("<I", 4) + row(5, 50001, 21004, 42) + row(5, 50002, 21004, 43) + \
            row(5, 50003, 20493, 42) + row(11, 50004, 21004, 42)
        self.assertEqual(connection_ports(42, 21004, table), {50001})  # established, to the Gateway, of pid 42
        self.assertEqual(connection_ports(43, 21004, table), {50002})
        self.assertEqual(connection_ports(44, 21004, table), set())


class ClientControlLoginTests(unittest.TestCase):
    def setUp(self):
        # Keep execute/ready/status/wait real without constructing a Frida device or attaching.
        self.control = ClientControl.__new__(ClientControl)
        self.control.script = object()
        self.control.rpc = Mock()
        self.elapsed = 0.0
        clock = patch("client_control.time.monotonic", side_effect=lambda: self.elapsed)
        clock.start()
        self.addCleanup(clock.stop)
        sleep = patch("client_control.time.sleep", side_effect=self.advance_time)
        self.sleep = sleep.start()
        self.addCleanup(sleep.stop)

    def advance_time(self, seconds):
        self.elapsed += seconds

    def execute_login(self, statuses):
        self.control.rpc.side_effect = chain([
            {"screen": "login", "loginState": 0},  # ready
            {},  # connect
            {"screen": "login", "loginState": 2, "lastLoginResult": None},  # handshake
            {},  # login
        ], statuses)
        return self.control.execute({"command": "login", "account": "test-only", "password": "test-only"})

    def test_login_success_before_authenticated_state_waits_for_character_selection(self):
        pending = {"screen": "login", "loginState": 2, "lastLoginResult": 0}
        authenticated = {**pending, "loginState": 3}
        characters = {**authenticated, "screen": "characters"}

        result = self.execute_login([pending, authenticated, authenticated, characters])

        self.assertEqual(result, {"running": True, **characters})
        self.assertEqual(self.sleep.call_args_list, [call(0.05), call(0.05)])
        self.assertEqual([c.args[0] for c in self.control.rpc.call_args_list],
                         ["status", "connect", "status", "login", "status", "status", "status", "status"])

    def test_login_nonzero_result_rejects_without_another_poll(self):
        with self.assertRaisesRegex(RuntimeError, r"^Local login rejected \(result 6\)$"):
            self.execute_login([{"screen": "login", "loginState": 2, "lastLoginResult": 6}])

        self.sleep.assert_not_called()
        self.assertEqual(self.control.rpc.call_count, 5)

    def test_login_without_authenticated_state_keeps_existing_timeout(self):
        for login_result in (None, 0):
            with self.subTest(lastLoginResult=login_result):
                self.elapsed = 0.0
                pending = {"screen": "login", "loginState": 2, "lastLoginResult": login_result}
                with self.assertRaisesRegex(TimeoutError, "Timed out waiting for login result;"):
                    self.execute_login(repeat(pending))

                self.assertGreaterEqual(self.elapsed, 15)
                self.assertLess(self.elapsed, 15.1)


if __name__ == "__main__":
    unittest.main()
