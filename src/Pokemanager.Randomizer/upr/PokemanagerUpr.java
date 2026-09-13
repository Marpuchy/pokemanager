// Bridge between Pokemanager and Universal Pokemon Randomizer ZX.
//
// Run with Java's source-file launcher (JDK 11+), no compilation needed:
//   java -Xmx4096M -cp PokeRandoZX.jar PokemanagerUpr.java <command> ...
//
// Commands:
//   randomize <settings.rnqs> <rom> <output> <seed> <log>
//       Like CliRandomizer, but with a fixed seed (Randomizer.randomize(file, log, seed)).
//       LayeredFS output: <output>/<TitleID>/{romfs/..., code.bin}.
//   pack <rom> <titleFolder> <output.cxi> <seed>
//       Base ROM + the files in <titleFolder> (romfs/... and code.bin) -> .cxi via ctr.NCCH.saveAsNCCH.
//   describe-settings <settings.rnqs | -> [rom]
//       Options of the preset (or of the defaults with "-") as JSON on standard output.
//       With a ROM, tells which misc tweaks that game supports.
//   write-settings <base.rnqs | -> <assignments.txt> <output.rnqs>
//       Applies "name=value" lines (and "tweak:FIELD=true|false") and writes the preset.
//
// Warnings are printed as "WARNING:<CODE>" so the application can show them in the user's language.

import com.dabomstew.pkrandom.FileFunctions;
import com.dabomstew.pkrandom.MiscTweak;
import com.dabomstew.pkrandom.RandomSource;
import com.dabomstew.pkrandom.Randomizer;
import com.dabomstew.pkrandom.Settings;
import com.dabomstew.pkrandom.ctr.NCCH;
import com.dabomstew.pkrandom.romhandlers.Abstract3DSRomHandler;
import com.dabomstew.pkrandom.romhandlers.Gen6RomHandler;
import com.dabomstew.pkrandom.romhandlers.RomHandler;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.PrintStream;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.ResourceBundle;
import java.util.TreeMap;
import java.util.stream.Stream;

public class PokemanagerUpr {
    public static void main(String[] args) {
        try {
            if (args.length == 0) usage();
            switch (args[0]) {
                case "randomize" -> { need(args, 6); randomize(args[1], args[2], args[3], Long.parseLong(args[4]), args[5]); }
                case "pack" -> { need(args, 5); pack(args[1], args[2], args[3], Long.parseLong(args[4])); }
                case "describe-settings" -> {
                    if (args.length != 2 && args.length != 3) usage();
                    describeSettings(args[1], args.length == 3 ? args[2] : null);
                }
                case "write-settings" -> { need(args, 4); writeSettings(args[1], args[2], args[3]); }
                default -> usage();
            }
        } catch (Exception e) {
            e.printStackTrace();
            System.exit(1);
        }
    }

    // ---------------------------------------------------------------- randomize

    private static void randomize(String settingsPath, String romPath, String output, long seed, String logPath) throws Exception {
        Settings settings = readSettings(settingsPath);
        settings.setCustomNames(FileFunctions.getCustomNames());
        RomHandler handler = loadRom(romPath);

        Settings.TweakForROMFeedback feedback = settings.tweakForRom(handler);
        if (feedback.isChangedStarter() && settings.getStartersMod() == Settings.StartersMod.CUSTOM) {
            System.out.println("WARNING:CUSTOM_STARTERS_CHANGED");
        }
        if (settings.isUpdatedFromOldVersion()) {
            System.out.println("WARNING:OLD_PRESET");
        }

        ByteArrayOutputStream logBytes = new ByteArrayOutputStream();
        PrintStream log = new PrintStream(logBytes, false, "UTF-8");
        ResourceBundle bundle = ResourceBundle.getBundle("com/dabomstew/pkrandom/newgui/Bundle");

        new Randomizer(settings, handler, bundle, true).randomize(new File(output).getAbsolutePath(), log, seed);
        log.close();
        Files.write(new File(logPath).toPath(), logBytes.toByteArray());
        System.out.println("OK " + seed);
    }

    // ---------------------------------------------------------------- pack

    private static void pack(String romPath, String titleDir, String outputCxi, long seed) throws Exception {
        RomHandler handler = loadRom(romPath);
        NCCH ncch = (NCCH) field(Abstract3DSRomHandler.class, "baseRom").get(handler);
        Method acronym = Abstract3DSRomHandler.class.getDeclaredMethod("getGameAcronym");
        acronym.setAccessible(true);

        Path title = Path.of(titleDir);
        Path romfs = title.resolve("romfs");
        int count = 0;
        if (Files.isDirectory(romfs)) {
            try (Stream<Path> files = Files.walk(romfs)) {
                for (Path file : (Iterable<Path>) files.filter(Files::isRegularFile)::iterator) {
                    String relative = romfs.relativize(file).toString().replace(File.separatorChar, '/');
                    ncch.writeFile(relative, Files.readAllBytes(file));
                    count++;
                }
            }
        }
        Path code = title.resolve("code.bin");
        if (Files.isRegularFile(code)) {
            ncch.writeCode(Files.readAllBytes(code));
            count++;
        }

        ncch.saveAsNCCH(new File(outputCxi).getAbsolutePath(), (String) acronym.invoke(handler), seed);
        System.out.println("OK " + count + " files");
    }

    // ---------------------------------------------------------------- settings

    /** Simple Settings options: get/is getter + public setter taking boolean, int or boolean... (enum). */
    private static Map<String, Method[]> options() {
        Map<String, Method[]> result = new TreeMap<>();
        for (Method setter : Settings.class.getMethods()) {
            if (!setter.getName().startsWith("set") || setter.getParameterCount() != 1 || Modifier.isStatic(setter.getModifiers())) continue;
            String name = setter.getName().substring(3);
            Class<?> p = setter.getParameterTypes()[0];
            if (!(p == boolean.class || p == int.class || p == boolean[].class)) continue;
            if (name.equals("CurrentMiscTweaks") || name.equals("UpdateMovesLegacy")) continue;
            Method getter = find("get" + name);
            if (getter == null) getter = find("is" + name);
            if (getter == null || getter.getParameterCount() != 0) continue;
            if (p == boolean[].class && !getter.getReturnType().isEnum()) continue;
            if (p != boolean[].class && getter.getReturnType() != p) continue;
            result.put(name, new Method[]{getter, setter});
        }
        return result;
    }

    private static void describeSettings(String settingsPath, String romPath) throws Exception {
        Settings settings = "-".equals(settingsPath) ? defaults() : readSettings(settingsPath);
        // With a ROM, mark which misc tweaks that game supports (UPR ignores the others).
        int available = romPath == null ? -1 : loadRom(romPath).miscTweaksAvailable();
        StringBuilder json = new StringBuilder("{\"version\":").append(Settings.VERSION).append(",\"options\":[");
        boolean first = true;
        for (Map.Entry<String, Method[]> e : options().entrySet()) {
            Method getter = e.getValue()[0];
            Object value = getter.invoke(settings);
            if (!first) json.append(',');
            first = false;
            json.append("{\"name\":").append(quote(e.getKey()));
            Class<?> type = getter.getReturnType();
            if (type == boolean.class) {
                json.append(",\"type\":\"bool\",\"value\":").append(value);
            } else if (type == int.class) {
                json.append(",\"type\":\"int\",\"value\":").append(value);
            } else {
                json.append(",\"type\":\"enum\",\"value\":").append(value == null ? "null" : quote(((Enum<?>) value).name())).append(",\"choices\":[");
                Object[] constants = type.getEnumConstants();
                for (int i = 0; i < constants.length; i++) {
                    if (i > 0) json.append(',');
                    json.append(quote(((Enum<?>) constants[i]).name()));
                }
                json.append(']');
            }
            json.append('}');
        }
        json.append("],\"tweaks\":[");
        int current = settings.getCurrentMiscTweaks();
        for (int i = 0; i < MiscTweak.allTweaks.size(); i++) {
            MiscTweak t = MiscTweak.allTweaks.get(i);
            if (i > 0) json.append(',');
            json.append("{\"name\":").append(quote(tweakField(t)))
                .append(",\"label\":").append(quote(t.getTweakName()))
                .append(",\"tooltip\":").append(quote(t.getTooltipText()))
                .append(",\"available\":").append((available & t.getValue()) != 0)
                .append(",\"value\":").append((current & t.getValue()) != 0).append('}');
        }
        json.append("]}");
        PrintStream out = new PrintStream(System.out, true, StandardCharsets.UTF_8);
        out.println(json);
    }

    private static void writeSettings(String basePath, String assignmentsPath, String outputPath) throws Exception {
        Settings settings = "-".equals(basePath) ? defaults() : readSettings(basePath);
        Map<String, Method[]> options = options();
        List<String> errors = new ArrayList<>();
        int tweaks = settings.getCurrentMiscTweaks();

        for (String raw : Files.readAllLines(Path.of(assignmentsPath), StandardCharsets.UTF_8)) {
            String line = raw.replace("﻿", "").trim(); // ignore a UTF-8 BOM
            if (line.isEmpty() || line.startsWith("#")) continue;
            int eq = line.indexOf('=');
            if (eq < 0) { errors.add("Line without '=': " + line); continue; }
            String name = line.substring(0, eq).trim(), value = line.substring(eq + 1).trim();

            if (name.startsWith("tweak:")) {
                MiscTweak t = tweakByField(name.substring(6));
                if (t == null) { errors.add("Unknown misc tweak: " + name); continue; }
                tweaks = Boolean.parseBoolean(value) ? (tweaks | t.getValue()) : (tweaks & ~t.getValue());
                continue;
            }

            Method[] m = options.get(name);
            if (m == null) { errors.add("Unknown option: " + name); continue; }
            Class<?> type = m[1].getParameterTypes()[0];
            if (type == boolean.class) m[1].invoke(settings, Boolean.parseBoolean(value));
            else if (type == int.class) m[1].invoke(settings, Integer.parseInt(value));
            else {
                Object[] constants = m[0].getReturnType().getEnumConstants();
                boolean[] selected = new boolean[constants.length];
                boolean found = false;
                for (int i = 0; i < constants.length; i++) {
                    selected[i] = ((Enum<?>) constants[i]).name().equals(value);
                    found |= selected[i];
                }
                if (!found) { errors.add("Invalid value for " + name + ": " + value); continue; }
                m[1].invoke(settings, (Object) selected);
            }
        }
        settings.setCurrentMiscTweaks(tweaks);

        if (!errors.isEmpty()) {
            errors.forEach(System.err::println);
            System.exit(4);
        }
        try (FileOutputStream out = new FileOutputStream(outputPath)) {
            settings.write(out);
        }
        System.out.println("OK");
    }

    // ---------------------------------------------------------------- helpers

    /** Default settings. new Settings() leaves null fields that UPR's GUI fills in and write() needs. */
    private static Settings defaults() {
        Settings settings = new Settings();
        if (settings.getSelectedEXPCurve() == null) settings.setSelectedEXPCurve(com.dabomstew.pkrandom.pokemon.ExpCurve.MEDIUM_FAST);
        if (settings.getRomName() == null) settings.setRomName("");
        return settings;
    }

    private static Settings readSettings(String path) throws Exception {
        try (FileInputStream in = new FileInputStream(path)) {
            return Settings.read(in);
        }
    }

    private static RomHandler loadRom(String romPath) {
        String rom = new File(romPath).getAbsolutePath();
        RomHandler.Factory factory = new Gen6RomHandler.Factory();
        if (!factory.isLoadable(rom)) fail("The ROM is not a Generation 6 game UPR ZX can load: " + rom);
        RomHandler handler = factory.create(RandomSource.instance());
        if (!handler.loadRom(rom)) fail("UPR ZX could not load the ROM: " + rom);
        return handler;
    }

    private static Method find(String name) {
        for (Method m : Settings.class.getMethods()) if (m.getName().equals(name) && m.getParameterCount() == 0) return m;
        return null;
    }

    private static Field field(Class<?> type, String name) throws NoSuchFieldException {
        Field f = type.getDeclaredField(name);
        f.setAccessible(true);
        return f;
    }

    private static String tweakField(MiscTweak tweak) throws IllegalAccessException {
        for (Field f : MiscTweak.class.getFields())
            if (Modifier.isStatic(f.getModifiers()) && f.getType() == MiscTweak.class && f.get(null) == tweak) return f.getName();
        return "TWEAK_" + tweak.getValue();
    }

    private static MiscTweak tweakByField(String name) throws IllegalAccessException {
        for (MiscTweak t : MiscTweak.allTweaks) if (tweakField(t).equals(name)) return t;
        return null;
    }

    private static String quote(String s) {
        if (s == null) return "null";
        StringBuilder b = new StringBuilder("\"");
        for (char c : s.toCharArray()) {
            switch (c) {
                case '"' -> b.append("\\\"");
                case '\\' -> b.append("\\\\");
                case '\n' -> b.append("\\n");
                case '\r' -> b.append("\\r");
                case '\t' -> b.append("\\t");
                default -> { if (c < 0x20) b.append(String.format("\\u%04x", (int) c)); else b.append(c); }
            }
        }
        return b.append('"').toString();
    }

    private static void need(String[] args, int count) {
        if (args.length != count) usage();
    }

    private static void usage() {
        System.err.println("Usage: PokemanagerUpr randomize|pack|describe-settings|write-settings ...");
        System.exit(2);
    }

    private static void fail(String message) {
        System.err.println(message);
        System.exit(3);
    }
}
