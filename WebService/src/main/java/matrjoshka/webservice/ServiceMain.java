package matrjoshka.webservice;

import org.springframework.boot.autoconfigure.EnableAutoConfiguration;
import org.springframework.boot.SpringApplication;
import org.springframework.context.annotation.ComponentScan;

/**
 * Created by jasmin on 29.11.14.
 */
@ComponentScan
@EnableAutoConfiguration
public class ServiceMain {

    public static void main(String[] args) {

        SpringApplication.run(ServiceMain.class, args);

    }

}
