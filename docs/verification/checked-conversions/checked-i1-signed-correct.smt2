(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (ite (= ((_ sign_extend 24) ((_ extract 7 0) input_0)) input_0)
                #b1
                #b0))
      (a!2 (bvnot (bvor (ite (bvslt input_0 #xffffff80) #b1 #b0)
                        (ite (bvslt #x0000007f input_0) #b1 #b0)))))
  (not (= a!1 a!2))))

(check-sat)
