package matrjoshka.webservice;

import org.apache.log4j.Logger;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestMethod;
import org.springframework.web.bind.annotation.RestController;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.util.ArrayList;

/**
 * Created by jasmin on 29.11.14.
 */
@RestController
@RequestMapping("/cite")
public class WebService {

    private static final Logger logger = Logger.getLogger(WebService.class);

    private ArrayList<String> quotes = new ArrayList<String>();

    public WebService() throws Exception {
        BufferedReader reader = new BufferedReader(new InputStreamReader(ServiceMain.class.getClassLoader().getResourceAsStream("quotes.txt")));
        String line;
        while((line = reader.readLine()) != null) {
            quotes.add(line);
        }
    }

    @RequestMapping(value = "/random", method = RequestMethod.GET)
    public String citeRandom() {
        String quote = quotes.get((int) (Math.random() * quotes.size()));
        logger.info("Respond with quote: " + quote);
        return quote;
    }

}
