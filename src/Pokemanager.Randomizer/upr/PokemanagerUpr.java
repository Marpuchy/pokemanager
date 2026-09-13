// Lanzador de Universal Pokemon Randomizer ZX con semilla fija.
//
// El CLI de UPR ZX (cli -s -i -o -d -u -l) elige la semilla al azar. Este lanzador repite lo que hace
// com.dabomstew.pkrandom.cli.CliRandomizer.performDirectRandomization, pero llama a
// Randomizer.randomize(archivo, log, semilla) para que el resultado sea reproducible.
//
// Se ejecuta con el lanzador de código fuente de Java (JDK 11+), sin compilar:
//   java -Xmx4096M -cp PokeRandoZX.jar PokemanagerUpr.java <ajustes.rnqs> <rom> <salida> <semilla> <log>
//
// La salida es siempre un directorio LayeredFS (juegos de 3DS).

import com.dabomstew.pkrandom.FileFunctions;
import com.dabomstew.pkrandom.RandomSource;
import com.dabomstew.pkrandom.Randomizer;
import com.dabomstew.pkrandom.Settings;
import com.dabomstew.pkrandom.romhandlers.Gen6RomHandler;
import com.dabomstew.pkrandom.romhandlers.RomHandler;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ResourceBundle;

public class PokemanagerUpr {
    public static void main(String[] args) {
        if (args.length != 5) {
            System.err.println("Uso: PokemanagerUpr <ajustes.rnqs> <rom> <salida> <semilla> <log>");
            System.exit(2);
        }

        try {
            Settings settings;
            try (FileInputStream in = new FileInputStream(args[0])) {
                settings = Settings.read(in);
            }
            settings.setCustomNames(FileFunctions.getCustomNames());

            String rom = new File(args[1]).getAbsolutePath();
            RomHandler.Factory factory = new Gen6RomHandler.Factory();
            if (!factory.isLoadable(rom)) {
                fail("La ROM no es un juego de 6.ª generación que UPR ZX pueda cargar: " + rom);
            }
            RomHandler handler = factory.create(RandomSource.instance());
            if (!handler.loadRom(rom)) {
                fail("UPR ZX no pudo cargar la ROM: " + rom);
            }

            Settings.TweakForROMFeedback feedback = settings.tweakForRom(handler);
            if (feedback.isChangedStarter() && settings.getStartersMod() == Settings.StartersMod.CUSTOM) {
                System.out.println("AVISO: los iniciales personalizados del preset no existen en esta ROM y se han cambiado.");
            }
            if (settings.isUpdatedFromOldVersion()) {
                System.out.println("AVISO: el preset es de una versión anterior de UPR ZX.");
            }

            ByteArrayOutputStream logBytes = new ByteArrayOutputStream();
            PrintStream log = new PrintStream(logBytes, false, "UTF-8");
            ResourceBundle bundle = ResourceBundle.getBundle("com/dabomstew/pkrandom/newgui/Bundle");
            long seed = Long.parseLong(args[3]);
            String output = new File(args[2]).getAbsolutePath();

            new Randomizer(settings, handler, bundle, true).randomize(output, log, seed);
            log.close();
            Files.write(new File(args[4]).toPath(), logBytes.toByteArray());

            System.out.println("OK " + seed);
        } catch (Exception e) {
            e.printStackTrace();
            System.exit(1);
        }
    }

    private static void fail(String message) {
        System.err.println(message);
        System.exit(3);
    }
}
