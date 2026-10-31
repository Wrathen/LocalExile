// Usage: -noanalysis -postScript BoundedFunctions.java <targets.json> <output.jsonl>
// Targets are supplied by damage_audit.py: hash-checked PE unwind roots/chained ranges.
// Only supplied functions are decompiled. This does not execute the target program.
import ghidra.app.script.GhidraScript;
import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.SourceType;
import com.google.gson.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.io.BufferedWriter;

public class BoundedFunctions extends GhidraScript {
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) throw new IllegalArgumentException("targets.json output.jsonl");
        Gson gson = new Gson();
        JsonArray targets = gson.fromJson(Files.readString(Path.of(args[0])), JsonArray.class);
        for (JsonElement element : targets) {
            JsonObject row = element.getAsJsonObject();
            Address entry = toAddr(row.get("start").getAsString());
            AddressSet body = new AddressSet();
            for (JsonElement part : row.getAsJsonArray("ranges")) {
                JsonObject range = part.getAsJsonObject();
                body.addRange(toAddr(range.get("start").getAsString()),
                    toAddr(range.get("endExclusive").getAsString()).subtract(1));
            }
            new DisassembleCommand(entry, body, true).applyTo(currentProgram, monitor);
            Function existing = currentProgram.getFunctionManager().getFunctionAt(entry);
            if (existing == null) {
                currentProgram.getFunctionManager().createFunction("poe_" + entry, entry, body, SourceType.USER_DEFINED);
            } else {
                existing.setBody(body);
            }
        }
        DecompInterface decompiler = new DecompInterface();
        decompiler.toggleCCode(true);
        decompiler.toggleSyntaxTree(false);
        decompiler.openProgram(currentProgram);
        try (BufferedWriter output = Files.newBufferedWriter(Path.of(args[1]), StandardCharsets.UTF_8)) {
            for (JsonElement element : targets) {
                JsonObject row = element.getAsJsonObject();
                if (!row.get("decompile").getAsBoolean()) continue;
                monitor.checkCancelled();
                Address entry = toAddr(row.get("start").getAsString());
                Function function = currentProgram.getFunctionManager().getFunctionAt(entry);
                DecompileResults code = decompiler.decompileFunction(function, 30, monitor);
                JsonObject result = new JsonObject();
                result.addProperty("address", "0x" + entry);
                result.addProperty("scope", row.get("scope").getAsString());
                result.add("ranges", row.getAsJsonArray("ranges"));
                result.addProperty("completed", code.decompileCompleted());
                result.addProperty("error", code.getErrorMessage());
                if (code.decompileCompleted() && code.getDecompiledFunction() != null)
                    result.addProperty("pseudocode", code.getDecompiledFunction().getC());
                output.write(gson.toJson(result));
                output.newLine();
                output.flush();
                println("BOUNDED decompiled " + entry);
            }
        } finally {
            decompiler.dispose();
        }
    }
}
